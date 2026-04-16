using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WhatsAppBot.Models.Flights;
using WhatsAppBot.Models.Passengers;
using WhatsAppBot.Services.Automation;
using WhatsAppBot.Services.CaptchaSolver;

namespace WhatsAppBot.Services.Flights.Automations
{
    public class TurkishAirlinesAutomation : IFlightBookingAutomation
    {
        public string AirlineKey => "turkish";
        private readonly SelfCaptchaSolver _captcha;
        private readonly StealthBrowserManager _browser;
        private readonly ILogger<TurkishAirlinesAutomation> _logger;

        public TurkishAirlinesAutomation(
            SelfCaptchaSolver captcha,
            StealthBrowserManager browser,
            ILogger<TurkishAirlinesAutomation> logger)
        {
            _captcha = captcha;
            _browser = browser;
            _logger = logger;
        }

        public async Task<FlightQuote?> SearchAsync(FlightSearchDraft draft, CancellationToken ct)
        {
            _logger.LogInformation("[Turkish] Searching {From}→{To} on {Date}",
                draft.From, draft.To, draft.DepartDate);

            try
            {
                await using var context = await _browser.CreateContextAsync();
                var page = await context.NewPageAsync();

                await page.GotoAsync(
                    $"https://www.turkishairlines.com/en-int/flights/",
                    new Microsoft.Playwright.PageGotoOptions
                    {
                        WaitUntil = Microsoft.Playwright.WaitUntilState.NetworkIdle,
                        Timeout = 30000
                    });

                // Solve any CAPTCHA
                var captchaResult = await _captcha.SolvePageChallengeAsync(page, ct);
                if (!captchaResult.Solved && captchaResult.Type != CaptchaSolver.CaptchaType.None)
                {
                    _logger.LogWarning("[Turkish] CAPTCHA not solved, falling back");
                    return null;
                }

                // Return deep link quote (browser confirms page loads)
                return new FlightQuote
                {
                    SourceKey = AirlineKey,
                    IsPriceExact = false,
                    BookingUrl = $"https://www.turkishairlines.com/en-int/flights/?origin={draft.From}&destination={draft.To}&departure={draft.DepartDate:yyyy-MM-dd}&adult={draft.Adults}",
                    Message = "Turkish Airlines booking page ready. Complete your booking on their site."
                };
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "[Turkish] Search failed");
                return null;
            }
        }

        public async Task<bool> ReserveAsync(FlightSearchDraft draft, PassengerInfo passenger, CancellationToken ct)
        {
            _logger.LogInformation("[Turkish] Attempting reservation for {Name}", passenger.FullName);

            try
            {
                await using var context = await _browser.CreateContextAsync();
                var page = await context.NewPageAsync();

                // Navigate to booking page
                await page.GotoAsync(
                    $"https://www.turkishairlines.com/en-int/flights/?origin={draft.From}&destination={draft.To}&departure={draft.DepartDate:yyyy-MM-dd}&adult={draft.Adults}",
                    new Microsoft.Playwright.PageGotoOptions
                    {
                        WaitUntil = Microsoft.Playwright.WaitUntilState.NetworkIdle,
                        Timeout = 30000
                    });

                var captchaResult = await _captcha.SolvePageChallengeAsync(page, ct);
                if (!captchaResult.Solved && captchaResult.Type != CaptchaSolver.CaptchaType.None)
                    return false;

                // TODO: Fill passenger form fields when we get the exact DOM structure
                // For now return false to use deep-link fallback
                _logger.LogInformation("[Turkish] Reservation flow not yet fully automated, using deep-link");
                return false;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "[Turkish] Reservation failed");
                return false;
            }
        }

        public Task<bool> CancelAsync(string bookingReference, CancellationToken ct)
        {
            _logger.LogInformation("[Turkish] Cancellation requested for {Ref}", bookingReference);
            return Task.FromResult(false);
        }
    }
}