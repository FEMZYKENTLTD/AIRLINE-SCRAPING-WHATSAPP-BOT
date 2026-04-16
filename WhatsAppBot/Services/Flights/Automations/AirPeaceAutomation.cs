using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WhatsAppBot.Models.Flights;
using WhatsAppBot.Models.Passengers;
using WhatsAppBot.Services.Automation;
using WhatsAppBot.Services.CaptchaSolver;

namespace WhatsAppBot.Services.Flights.Automations
{
    public class AirPeaceAutomation : IFlightBookingAutomation
    {
        public string AirlineKey => "airpeace";
        private readonly SelfCaptchaSolver _captcha;
        private readonly StealthBrowserManager _browser;
        private readonly ILogger<AirPeaceAutomation> _logger;

        public AirPeaceAutomation(
            SelfCaptchaSolver captcha,
            StealthBrowserManager browser,
            ILogger<AirPeaceAutomation> logger)
        {
            _captcha = captcha;
            _browser = browser;
            _logger = logger;
        }

        public async Task<FlightQuote?> SearchAsync(FlightSearchDraft draft, CancellationToken ct)
        {
            _logger.LogInformation("[AirPeace] Searching {From}→{To}", draft.From, draft.To);

            try
            {
                await using var context = await _browser.CreateContextAsync();
                var page = await context.NewPageAsync();

                await page.GotoAsync("https://flyairpeace.com/",
                    new Microsoft.Playwright.PageGotoOptions
                    {
                        WaitUntil = Microsoft.Playwright.WaitUntilState.NetworkIdle,
                        Timeout = 30000
                    });

                await _captcha.SolvePageChallengeAsync(page, ct);

                return new FlightQuote
                {
                    SourceKey = AirlineKey,
                    IsPriceExact = false,
                    BookingUrl = $"https://flyairpeace.com/booking?from={draft.From}&to={draft.To}&date={draft.DepartDate:yyyy-MM-dd}&adults={draft.Adults}",
                    Message = "Air Peace booking page ready."
                };
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "[AirPeace] Search failed");
                return null;
            }
        }

        public Task<bool> ReserveAsync(FlightSearchDraft draft, PassengerInfo passenger, CancellationToken ct)
        {
            _logger.LogInformation("[AirPeace] Reservation not yet automated");
            return Task.FromResult(false);
        }

        public Task<bool> CancelAsync(string bookingReference, CancellationToken ct)
        {
            return Task.FromResult(false);
        }
    }
}