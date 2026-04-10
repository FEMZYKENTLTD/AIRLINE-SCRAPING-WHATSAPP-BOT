namespace WhatsAppBot.Models.Flights
{
    public enum FlightStep
    {
        None = 0,
        ChooseAirline = 1,
        ChoosePricingMode = 2,
        TripType = 3,
        FromAirport = 4,
        ToAirport = 5,
        DepartDate = 6,
        ReturnDate = 7,
        Passengers = 8,
        Confirm = 9
    }
}
