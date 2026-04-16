namespace WhatsAppBot.Models.Payments
{
    public enum PaymentStatus
    {
        Pending = 0,
        Processing = 1,
        Succeeded = 2,
        Failed = 3,
        Refunded = 4,
        PartialRefund = 5,
        Cancelled = 6
    }
}