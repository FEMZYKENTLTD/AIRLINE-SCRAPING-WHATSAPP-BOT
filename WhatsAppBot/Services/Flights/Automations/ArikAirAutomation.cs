using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WhatsAppBot.Models.Flights;
using WhatsAppBot.Models.Passengers;
using WhatsAppBot.Services.Automation;
using WhatsAppBot.Services.CaptchaSolver;

namespace WhatsAppBot.Services.Flights.Automations
{
    public class ArikAirAutomation : IFlightBookingAutomation
    {
        public string AirlineKey => "arikair";
        private readonly SelfCaptchaSolver _captcha;
        private readonly StealthBrowserManager _browser;
        private readonly ILogger<ArikAirAutomation> _logger;

        public ArikAirAutomation(
            SelfCaptchaSolver captcha,
            StealthBrowserManager browser,
            ILogger<ArikAirAutomation> logger)
        {
            _captcha = captcha;
            _browser = browser;
            _logger = logger;
        }

        public async Task<FlightQuote?> SearchAsync(FlightSearchDraft draft, CancellationToken ct)
        {
            _logger.LogInformation("[ArikAir] Searching {From}→{To}", draft.From, draft.To);

            try
            {
                await using var context = await _browser.CreateContextAsync();
                var page = await context.NewPageAsync();

                await page.GotoAsync("https://www.arikair.com/",
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
                    BookingUrl = $"https://www.arikair.com/plan-and-book?origin={draft.From}&destination={draft.To}&date={draft.DepartDate:yyyy-MM-dd}",
                    Message = "Arik Air booking page ready."
                };
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "[ArikAir] Search failed");
                return null;
            }
        }

        public Task<bool> ReserveAsync(FlightSearchDraft draft, PassengerInfo passenger, CancellationToken ct)
        {
            return Task.FromResult(false);
        }

        public Task<bool> CancelAsync(string bookingReference, CancellationToken ct)
        {
            return Task.FromResult(false);
        }
    }
}