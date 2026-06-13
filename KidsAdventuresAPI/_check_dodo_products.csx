#r "nuget: DodoPayments.Client, 6.20.0"
using DodoPayments.Client;
using DodoPayments.Client.Models.Products;

var client = new DodoPaymentsClient
{
    BearerToken = Environment.GetEnvironmentVariable("DODO_API_KEY") ?? "",
    BaseUrl = "https://live.dodopayments.com"
};

var ids = new[] {
    ("Books3", "pdt_0NgxnLf02pWXtMSETBHKB"),
    ("Books5", "pdt_0NglOEG2SUeo9GJj9RO1a"),
    ("Books15", "pdt_0NglPAIK0sh0KqR6h6Phn"),
};

foreach (var (name, id) in ids)
{
    var product = await client.Products.Retrieve(new ProductRetrieveParams { ProductID = id });
    var priceText = "unknown";
    if (product.Price?.TryPickOneTime(out var oneTime) == true)
    {
        priceText = $"{oneTime.PriceValue} {oneTime.Currency}";
    }
    Console.WriteLine($"{name}: {product.Name} ({id}) => {priceText}");
}
