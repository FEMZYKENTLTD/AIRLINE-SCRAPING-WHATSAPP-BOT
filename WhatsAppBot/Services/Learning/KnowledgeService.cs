using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WhatsAppBot.Data;
using WhatsAppBot.Models.Learning;

namespace WhatsAppBot.Services.Learning
{
    public class KnowledgeService : IKnowledgeService
    {
        private readonly AppDbContext _db;
        private readonly IEmbeddingService _embedding;
        private readonly ILogger<KnowledgeService> _logger;

        public KnowledgeService(
            AppDbContext db,
            IEmbeddingService embedding,
            ILogger<KnowledgeService> logger)
        {
            _db = db;
            _embedding = embedding;
            _logger = logger;
        }

        public async Task<string?> FindAnswerAsync(string question, string? airlineKey = null)
        {
            if (string.IsNullOrWhiteSpace(question)) return null;

            // Step 1: Try exact keyword match
            var keyword = question.ToLowerInvariant();
            var directMatch = await _db.KnowledgeEntries
                .Where(k => k.IsActive &&
                    (airlineKey == null || k.AirlineKey == airlineKey) &&
                    (EF.Functions.Like(k.Question.ToLower(), $"%{keyword}%") ||
                     EF.Functions.Like(k.Answer.ToLower(), $"%{keyword}%")))
                .OrderByDescending(k => k.Relevance)
                .ThenByDescending(k => k.UseCount)
                .FirstOrDefaultAsync();

            if (directMatch != null)
            {
                directMatch.UseCount++;
                directMatch.LastUsedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                _logger.LogInformation("Knowledge hit (keyword): {Q}", directMatch.Question);
                return directMatch.Answer;
            }

            // Step 2: Try semantic search (embeddings)
            var allEntries = await _db.KnowledgeEntries
                .Where(k => k.IsActive && k.EmbeddingJson != null)
                .Select(k => new { k.Id, k.EmbeddingJson, k.Answer })
                .ToListAsync();

            if (allEntries.Count > 0)
            {
                var candidates = allEntries
                    .Where(e => !string.IsNullOrEmpty(e.EmbeddingJson))
                    .Select(e => (e.Id, Embedding: JsonSerializer.Deserialize<float[]>(e.EmbeddingJson!)!))
                    .ToList();

                var matches = await _embedding.FindSimilarAsync(question, candidates, 3);

                if (matches.Count > 0)
                {
                    var bestId = matches[0].Id;
                    var best = allEntries.First(e => e.Id == bestId);

                    var entry = await _db.KnowledgeEntries.FindAsync(bestId);
                    if (entry != null)
                    {
                        entry.UseCount++;
                        entry.LastUsedAtUtc = DateTime.UtcNow;
                        await _db.SaveChangesAsync();
                    }

                    _logger.LogInformation("Knowledge hit (semantic, score={Score}): ID={Id}",
                        matches[0].Score, bestId);
                    return best.Answer;
                }
            }

            return null;
        }

        public async Task AddKnowledgeAsync(
            string category, string question, string answer,
            string source, string? airlineKey = null, string? sourceUrl = null)
        {
            // Check for duplicate
            var exists = await _db.KnowledgeEntries.AnyAsync(k =>
                k.Question == question && k.Source == source);

            if (exists) return;

            // Generate embedding
            float[]? embedding = null;
            string? embeddingJson = null;

            try
            {
                embedding = await _embedding.GetEmbeddingAsync(question + " " + answer);
                if (embedding.Length > 0)
                    embeddingJson = JsonSerializer.Serialize(embedding);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate embedding for knowledge entry");
            }

            var entry = new KnowledgeEntry
            {
                Category = category,
                Question = question,
                Answer = answer,
                Source = source,
                AirlineKey = airlineKey,
                SourceUrl = sourceUrl,
                EmbeddingJson = embeddingJson,
                CreatedAtUtc = DateTime.UtcNow
            };

            _db.KnowledgeEntries.Add(entry);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Knowledge added: [{Category}] {Question}", category, question);
        }

        public async Task LearnFromConversationAsync(
            string phoneNumber, string userMessage, string botResponse, CancellationToken ct)
        {
            try
            {
                // Detect user preferences
                var lower = userMessage.ToLowerInvariant();

                // Detect preferred airline
                var airlineKeywords = new Dictionary<string, string>
                {
                    { "air peace", "airpeace" },
                    { "airpeace", "airpeace" },
                    { "turkish", "turkish" },
                    { "lufthansa", "lufthansa" },
                    { "arik", "arikair" }
                };

                foreach (var (keyword, airlineKey) in airlineKeywords)
                {
                    if (lower.Contains(keyword))
                    {
                        await SetPreferenceAsync(phoneNumber, "preferred_airline", airlineKey);
                        break;
                    }
                }

                // Detect common routes (IATA codes)
                var iataMatches = Regex.Matches(userMessage, @"\b([A-Z]{3})\b");
                if (iataMatches.Count >= 2)
                {
                    await SetPreferenceAsync(phoneNumber, "common_origin",
                        iataMatches[0].Value);
                    await SetPreferenceAsync(phoneNumber, "common_destination",
                        iataMatches[1].Value);
                }

                // Store conversation insight
                var insight = new ConversationInsight
                {
                    PhoneNumber = phoneNumber,
                    TopicDetected = DetectTopic(lower),
                    IntentDetected = DetectIntent(lower),
                    SentimentDetected = DetectSentiment(lower),
                    CreatedAtUtc = DateTime.UtcNow
                };

                _db.ConversationInsights.Add(insight);
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Learning from conversation failed");
            }
        }

        public async Task<UserPreference?> GetPreferenceAsync(string phoneNumber, string key)
        {
            return await _db.UserPreferences
                .Where(p => p.PhoneNumber == phoneNumber && p.PreferenceKey == key)
                .OrderByDescending(p => p.Confidence)
                .FirstOrDefaultAsync();
        }

        public async Task SetPreferenceAsync(string phoneNumber, string key, string value)
        {
            var existing = await _db.UserPreferences
                .FirstOrDefaultAsync(p => p.PhoneNumber == phoneNumber && p.PreferenceKey == key);

            if (existing != null)
            {
                if (existing.PreferenceValue == value)
                {
                    existing.Confidence++;
                }
                else
                {
                    existing.PreferenceValue = value;
                    existing.Confidence = 1;
                }
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }
            else
            {
                _db.UserPreferences.Add(new UserPreference
                {
                    PhoneNumber = phoneNumber,
                    PreferenceKey = key,
                    PreferenceValue = value,
                    Confidence = 1
                });
            }

            await _db.SaveChangesAsync();
        }

        public async Task<List<UserPreference>> GetAllPreferencesAsync(string phoneNumber)
        {
            return await _db.UserPreferences
                .Where(p => p.PhoneNumber == phoneNumber)
                .OrderByDescending(p => p.Confidence)
                .ToListAsync();
        }

        private static string DetectTopic(string text)
        {
            if (text.Contains("flight") || text.Contains("fly") || text.Contains("travel"))
                return "flights";
            if (text.Contains("hotel") || text.Contains("accommodation"))
                return "hotels";
            if (text.Contains("price") || text.Contains("cost") || text.Contains("cheap"))
                return "pricing";
            if (text.Contains("book") || text.Contains("reserve"))
                return "booking";
            if (text.Contains("cancel"))
                return "cancellation";
            return "general";
        }

        private static string DetectIntent(string text)
        {
            if (text.Contains("how much") || text.Contains("price") || text.Contains("cost"))
                return "price_inquiry";
            if (text.Contains("book") || text.Contains("reserve") || text.Contains("buy"))
                return "booking";
            if (text.Contains("cancel") || text.Contains("refund"))
                return "cancellation";
            if (text.Contains("when") || text.Contains("schedule") || text.Contains("time"))
                return "schedule";
            if (text.Contains("help") || text.Contains("how"))
                return "help";
            return "chat";
        }

        private static string DetectSentiment(string text)
        {
            var positive = new[] { "thanks", "great", "awesome", "good", "love", "nice", "perfect", "amazing" };
            var negative = new[] { "bad", "terrible", "awful", "hate", "worst", "angry", "frustrated", "disappointed" };

            if (positive.Any(w => text.Contains(w))) return "positive";
            if (negative.Any(w => text.Contains(w))) return "negative";
            return "neutral";
        }
    }
}