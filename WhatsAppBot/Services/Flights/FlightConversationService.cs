using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppBot.Models;
using WhatsAppBot.Models.Flights;
using WhatsAppBot.Services.Scraping;

namespace WhatsAppBot.Services.Flights
{
    public class FlightConversationService
    {
        private readonly FlightPricingService _pricing;
        private readonly ScrapingOptions _scrapeOpt;
        private readonly ILogger<FlightConversationService> _logger;

        public FlightConversationService(
            FlightPricingService pricing,
            IOptions<ScrapingOptions> scrapingOptions,
            ILogger<FlightConversationService> logger)
        {
            _pricing = pricing;
            _scrapeOpt = scrapingOptions.Value;
            _logger = logger;
        }

        public bool IsFlightFlowActive(UserSession session)
            => session.FlightStep != FlightConversationStep.None;

        public Task<string> StartAsync(UserSession session)
        {
            session.FlightDraft.Reset();
            session.FlightStep = FlightConversationStep.AwaitingMode;

            return Task.FromResult(
                "✈️ *Flight Search*\n\n" +
                "Pick pricing mode:\n" +
                "1) auto (recommended)\n" +
                "2) amadeus (best live pricing)\n" +
                "3) deeplink (fastest)\n" +
                "4) scrape (best-effort promos)\n\n" +
                "Reply with: `auto` / `amadeus` / `deeplink` / `scrape`");
        }

        public async Task<string> HandleAsync(UserSession session, string text, CancellationToken ct)
        {
            text = (text ?? "").Trim();

            switch (session.FlightStep)
            {
                case FlightConversationStep.AwaitingMode:
                    {
                        var mode = text.ToLowerInvariant();
                        if (mode is not ("auto" or "amadeus" or "deeplink" or "scrape"))
                            return "Reply with: `auto` / `amadeus` / `deeplink` / `scrape`";

                        session.FlightPricingMode = mode;
                        session.FlightStep = FlightConversationStep.AwaitingTripType;
                        return "Trip type? Reply: `oneway` or `return`";
                    }

                case FlightConversationStep.AwaitingTripType:
                    {
                        var t = text.ToLowerInvariant();
                        if (t is not ("oneway" or "return"))
                            return "Reply: `oneway` or `return`";

                        session.FlightDraft.TripType = t;
                        session.FlightStep = FlightConversationStep.AwaitingFrom;
                        return "From (IATA code). Example: `LOS`";
                    }

                case FlightConversationStep.AwaitingFrom:
                    {
                        var code = NormalizeIata(text);
                        if (code == null) return "Use a 3-letter airport code. Example: `LOS`";

                        session.FlightDraft.From = code;
                        session.FlightStep = FlightConversationStep.AwaitingTo;
                        return "To (IATA code). Example: `LHR`";
                    }

                case FlightConversationStep.AwaitingTo:
                    {
                        var code = NormalizeIata(text);
                        if (code == null) return "Use a 3-letter airport code. Example: `LHR`";

                        if (code == session.FlightDraft.From)
                            return "From and To can’t be the same. Enter a different destination code.";

                        session.FlightDraft.To = code;
                        session.FlightStep = FlightConversationStep.AwaitingDepartDate;
                        return "Departure date (YYYY-MM-DD). Example: `2026-03-10`";
                    }

                case FlightConversationStep.AwaitingDepartDate:
                    {
                        var d = ParseDate(text);
                        if (d == null) return "Enter date like `YYYY-MM-DD` (example: 2026-03-10).";

                        session.FlightDraft.DepartDate = d.Value;

                        if (session.FlightDraft.TripType == "return")
                        {
                            session.FlightStep = FlightConversationStep.AwaitingReturnDate;
                            return "Return date (YYYY-MM-DD). Example: `2026-03-20`";
                        }

                        session.FlightStep = FlightConversationStep.AwaitingPassengers;
                        return "Passengers (1-9). Example: `1`";
                    }

                case FlightConversationStep.AwaitingReturnDate:
                    {
                        var d = ParseDate(text);
                        if (d == null) return "Enter return date like `YYYY-MM-DD` (example: 2026-03-20).";

                        if (session.FlightDraft.DepartDate != null && d.Value < session.FlightDraft.DepartDate.Value)
                            return "Return date can’t be before departure date. Try again.";

                        session.FlightDraft.ReturnDate = d.Value;
                        session.FlightStep = FlightConversationStep.AwaitingPassengers;
                        return "Passengers (1-9). Example: `1`";
                    }

                case FlightConversationStep.AwaitingPassengers:
                    {
                        if (!int.TryParse(text, out var pax) || pax < 1 || pax > 9)
                            return "Passengers must be 1 to 9. Example: `2`";

                        session.FlightDraft.Passengers = pax;
                        session.FlightStep = FlightConversationStep.AwaitingCabin;
                        return "Cabin? Reply: `economy` / `premium` / `business` / `first`";
                    }

                case FlightConversationStep.AwaitingCabin:
                    {
                        var c = text.ToLowerInvariant();
                        if (c is not ("economy" or "premium" or "business" or "first"))
                            return "Reply: `economy` / `premium` / `business` / `first`";

                        session.FlightDraft.Cabin = c;
                        session.FlightStep = FlightConversationStep.AwaitingAirlineChoice;

                        var airlines = _scrapeOpt.Airlines ?? new List<AirlineTarget>();
                        if (airlines.Count == 0)
                        {
                            session.FlightDraft.AirlineSourceKey = "all";
                            session.FlightStep = FlightConversationStep.ReadyToQuote;
                            return await QuoteAsync(session, ct);
                        }

                        var sb = new StringBuilder();
                        sb.AppendLine("Which airline?");
                        sb.AppendLine("Reply: `all` or one of these keys:");
                        foreach (var a in airlines)
                            sb.AppendLine($"• {a.SourceKey} ({a.Name})");

                        return sb.ToString();
                    }

                case FlightConversationStep.AwaitingAirlineChoice:
                    {
                        var choice = text.ToLowerInvariant();
                        var airlines = _scrapeOpt.Airlines ?? new List<AirlineTarget>();

                        if (choice != "all" && !airlines.Any(a => a.SourceKey.Equals(choice, StringComparison.OrdinalIgnoreCase)))
                            return "Reply `all` or a valid airline key from the list I gave you.";

                        session.FlightDraft.AirlineSourceKey = choice;
                        session.FlightStep = FlightConversationStep.ReadyToQuote;
                        return await QuoteAsync(session, ct);
                    }

                case FlightConversationStep.ReadyToQuote:
                    return await QuoteAsync(session, ct);

                default:
                    session.FlightStep = FlightConversationStep.None;
                    return "Flight flow reset. Type `/flight` to start again.";
            }
        }

        private async Task<string> QuoteAsync(UserSession session, CancellationToken ct)
        {
            var airlines = _scrapeOpt.Airlines ?? new List<AirlineTarget>();
            if (airlines.Count == 0)
            {
                session.FlightStep = FlightConversationStep.None;
                return "No airlines configured in appsettings under `Scraping:Airlines`.";
            }

            var targets = session.FlightDraft.AirlineSourceKey == "all"
                ? airlines
                : airlines.Where(a => a.SourceKey.Equals(session.FlightDraft.AirlineSourceKey, StringComparison.OrdinalIgnoreCase)).ToList();

            var draft = session.FlightDraft;

            var sb = new StringBuilder();
            sb.AppendLine("🔎 *Searching flights...*");
            sb.AppendLine($"Route: {draft.From} → {draft.To}");
            sb.AppendLine($"Date: {draft.DepartDate:yyyy-MM-dd}" + (draft.TripType == "return" ? $" → {draft.ReturnDate:yyyy-MM-dd}" : ""));
            sb.AppendLine($"Passengers: {draft.Passengers}, Cabin: {draft.Cabin}");
            sb.AppendLine($"Mode: {session.FlightPricingMode}");
            sb.AppendLine();

            foreach (var airline in targets)
            {
                var q = await _pricing.GetQuoteAsync(airline, draft, session.FlightPricingMode, ct);

                sb.AppendLine($"*{airline.Name}* ({airline.SourceKey})");
                sb.AppendLine($"Provider: {q.Provider}");

                if (q.Amount != null)
                    sb.AppendLine($"Price: {q.Currency} {q.Amount:0,0} {(q.IsPriceExact ? "(exact)" : "(estimate)")}");

                sb.AppendLine(q.Message);

                if (!string.IsNullOrWhiteSpace(q.BookingUrl))
                    sb.AppendLine($"Book: {q.BookingUrl}");

                sb.AppendLine();
            }

            session.FlightStep = FlightConversationStep.None;
            return sb.ToString().Trim();
        }

        private static string? NormalizeIata(string input)
        {
            var s = (input ?? "").Trim().ToUpperInvariant();
            if (s.Length != 3) return null;
            if (!s.All(char.IsLetter)) return null;
            return s;
        }

        private static DateOnly? ParseDate(string input)
        {
            input = (input ?? "").Trim();
            if (DateOnly.TryParseExact(input, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return d;
            return null;
        }
    }
}
