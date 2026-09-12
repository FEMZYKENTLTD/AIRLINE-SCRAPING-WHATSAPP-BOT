namespace WhatsAppBot.Models.Flights
{
    /// <summary>
    /// Flight conversation flow steps used by FlightConversationService.
    /// Separate from FlightStep used by the WhatsAppWebhookController.
    /// </summary>
    public enum FlightConversationStep
    {
        None = 0,
        AwaitingMode = 1,
        AwaitingTripType = 2,
        AwaitingFrom = 3,
        AwaitingTo = 4,
        AwaitingDepartDate = 5,
        AwaitingReturnDate = 6,
        AwaitingPassengers = 7,
        AwaitingCabin = 8,
        AwaitingAirlineChoice = 9,
        ReadyToQuote = 10
    }
}
