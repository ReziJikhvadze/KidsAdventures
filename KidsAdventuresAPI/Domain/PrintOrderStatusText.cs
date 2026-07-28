namespace AdventurePacks.Api.Domain;

/// <summary>
/// Georgian labels for parcel statuses. Server-side rather than in the client catalogue
/// because the same words go into the notification emails, which no browser renders.
/// </summary>
public static class PrintOrderStatusText
{
    public static string Label(PrintOrderStatus status) => status switch
    {
        PrintOrderStatus.AwaitingPrint => "ბეჭდვის რიგში",
        PrintOrderStatus.Printing => "იბეჭდება",
        PrintOrderStatus.Shipped => "გზაშია",
        PrintOrderStatus.Delivered => "მიწოდებულია",
        PrintOrderStatus.Cancelled => "გაუქმებულია",
        _ => status.ToString()
    };
}
