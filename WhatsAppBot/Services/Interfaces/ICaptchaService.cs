namespace WhatsAppBot.Services.Interfaces
{
    public interface ICaptchaService
    {
        Task<string> SolveRecaptchaV2Async(string siteKey, string pageUrl);
        Task<string> SolveRecaptchaV3Async(string siteKey, string pageUrl, string action = "verify");
        Task<string> SolveHCaptchaAsync(string siteKey, string pageUrl);
        Task<string> SolveImageCaptchaAsync(byte[] imageBytes);
    }
}