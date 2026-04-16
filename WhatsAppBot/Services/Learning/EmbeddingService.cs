using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WhatsAppBot.Services.Learning
{
    public class EmbeddingService : IEmbeddingService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;
        private readonly ILogger<EmbeddingService> _logger;

        public EmbeddingService(
            HttpClient http,
            IConfiguration config,
            ILogger<EmbeddingService> logger)
        {
            _http = http;
            _config = config;
            _logger = logger;
        }

        public async Task<float[]> GetEmbeddingAsync(string text)
        {
            var endpoint = _config["AzureOpenAI:Endpoint"]?.TrimEnd('/');
            var apiKey = _config["AzureOpenAI:ApiKey"];
            var deployment = Environment.GetEnvironmentVariable("AZURE_EMBEDDING_DEPLOYMENT")
                             ?? "text-embedding";
            var apiVersion = _config["AzureOpenAI:ApiVersion"] ?? "2024-02-15-preview";

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("Embedding service not configured, returning empty");
                return Array.Empty<float>();
            }

            var url = $"{endpoint}/openai/deployments/{deployment}/embeddings?api-version={apiVersion}";

            var payload = new { input = text, model = deployment };

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("api-key", apiKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                var response = await _http.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Embedding API failed: {Status} {Body}",
                        response.StatusCode, json);
                    return Array.Empty<float>();
                }

                using var doc = JsonDocument.Parse(json);
                var embeddingArray = doc.RootElement
                    .GetProperty("data")[0]
                    .GetProperty("embedding");

                var embedding = new float[embeddingArray.GetArrayLength()];
                int i = 0;
                foreach (var val in embeddingArray.EnumerateArray())
                {
                    embedding[i++] = val.GetSingle();
                }

                return embedding;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Embedding generation failed");
                return Array.Empty<float>();
            }
        }

        public double CosineSimilarity(float[] a, float[] b)
        {
            if (a.Length == 0 || b.Length == 0 || a.Length != b.Length)
                return 0;

            double dot = 0, magA = 0, magB = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                magA += a[i] * a[i];
                magB += b[i] * b[i];
            }

            var magnitude = Math.Sqrt(magA) * Math.Sqrt(magB);
            return magnitude == 0 ? 0 : dot / magnitude;
        }

        public async Task<List<(int Id, double Score)>> FindSimilarAsync(
            string query, List<(int Id, float[] Embedding)> candidates, int topK = 5)
        {
            var queryEmbedding = await GetEmbeddingAsync(query);
            if (queryEmbedding.Length == 0)
                return new List<(int, double)>();

            return candidates
                .Select(c => (c.Id, Score: CosineSimilarity(queryEmbedding, c.Embedding)))
                .OrderByDescending(x => x.Score)
                .Take(topK)
                .Where(x => x.Score > 0.7)
                .ToList();
        }
    }
}