namespace WhatsAppBot.Models
{
    public enum UserState
    {
        New = 0,
        AwaitingName = 1,
        AwaitingEmail = 2,
        Verified = 3
    }
}