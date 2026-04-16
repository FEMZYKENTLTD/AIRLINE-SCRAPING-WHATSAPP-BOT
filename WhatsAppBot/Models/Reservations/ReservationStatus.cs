namespace WhatsAppBot.Models.Reservations
{
    public enum ReservationStatus
    {
        Draft = 0,
        PendingPayment = 1,
        PaymentReceived = 2,
        BookingInProgress = 3,
        Confirmed = 4,
        TicketIssued = 5,
        CancellationRequested = 6,
        Cancelled = 7,
        Refunded = 8,
        Failed = 9,
        Expired = 10
    }
}