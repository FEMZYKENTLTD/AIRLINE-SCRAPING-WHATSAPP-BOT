namespace WhatsAppBot.Models.Flights
{
    public enum FlightStep
    {
        None = 0,

        // Search flow
        ChooseAirline = 1,
        ChoosePricingMode = 2,
        TripType = 3,
        FromAirport = 4,
        ToAirport = 5,
        DepartDate = 6,
        ReturnDate = 7,
        Passengers = 8,
        Confirm = 9,

        // Booking flow
        ViewResults = 10,
        CollectFullName = 11,
        CollectDateOfBirth = 12,
        CollectGender = 13,
        CollectNationality = 14,
        CollectPassport = 15,
        CollectPassportExpiry = 16,
        CollectEmail = 17,
        CollectSeatPreference = 18,
        ConfirmBooking = 19,
        ProcessingPayment = 20,
        BookingComplete = 21,

        // Cancellation flow
        CancelConfirm = 30,
        CancelProcessing = 31
    }
}