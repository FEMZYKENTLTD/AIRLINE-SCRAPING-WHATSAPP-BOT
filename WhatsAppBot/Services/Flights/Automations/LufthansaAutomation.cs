using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WhatsAppBot.Models.Flights;
using WhatsAppBot.Models.Passengers;
using WhatsAppBot.Services.Automation;
using WhatsAppBot.Services.CaptchaSolver;

namespace WhatsAppBot.Services.Flights.Automations
{
    public class LufthansaAutomation : IFlightBookingAutomation
    {
        public string AirlineKey => "lufthansa";
        private readonly SelfCaptchaSolver _captcha;
        private readonly StealthBrowserManager _browser;
        private readonly ILogger<LufthansaAutomation> _logger;

        public LufthansaAutomation(
            SelfCaptchaSolver captcha,
            StealthBrowserManager browser,
            ILogger<LufthansaAutomation> logger)
        {
            _captcha = captcha;
            _browser = browser;
            _logger = logger;
        }

        public async Task<FlightQuote?> SearchAsync(FlightSearchDraft draft, CancellationToken ct)
        {
            _logger.LogInformation("[Lufthansa] Searching {From}→{To}", draft.From, draft.To);

            try
            {
                await using var context = await _browser.CreateContextAsync();
                var page = await context.NewPageAsync();

                await page.GotoAsync("https://www.lufthansa.com/ng/en/homepage",
                    new Microsoft.Playwright.PageGotoOptions
                    {
                        WaitUntil = Microsoft.Playwright.WaitUntilState.NetworkIdle,
                        Timeout = 30000
                    });

                var captchaResult = await _captcha.SolvePageChallengeAsync(page, ct);

                return new FlightQuote
                {
                    SourceKey = AirlineKey,
                    IsPriceExact = false,
                    BookingUrl = $"https://www.lufthansa.com/ng/en/flight-search?origin={draft.From}&destination={draft.To}&outbound-date={draft.DepartDate:yyyy-MM-dd}&pax={draft.Adults}",
                    Message = "Lufthansa booking page ready."
                };
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "[Lufthansa] Search failed");
                return null;
            }
        }

        public Task<bool> ReserveAsync(FlightSearchDraft draft, PassengerInfo passenger, CancellationToken ct)
        {
            _logger.LogInformation("[Lufthansa] Reservation not yet automated");
            return Task.FromResult(false);
        }

        public Task<bool> CancelAsync(string bookingReference, CancellationToken ct)
        {
            _logger.LogInformation("[Lufthansa] Cancellation: {Ref}", bookingReference);
            return Task.FromResult(false);
        }
    }
}