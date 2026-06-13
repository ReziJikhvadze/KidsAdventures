using DodoPayments.Client;
using DodoPayments.Client.Models.Payments;

var client = new DodoPaymentsClient {
    BearerToken = "QWpC_sbU5vxWJs3e.cohHsYpZiY0kBjISzaXs2zTsgDdaBK1NQ5dt1I6Jk13QOiR_",
    BaseUrl = "https://live.dodopayments.com"
};
var payment = await client.Payments.Retrieve(new PaymentRetrieveParams { PaymentID = "pay_0Ngy44CAp8QHexBULU2zL" });
Console.WriteLine($"Status type: {payment.Status?.GetType().FullName}");
Console.WriteLine($"Status ToString: {payment.Status?.ToString()}");
Console.WriteLine($"Metadata userId: {(payment.Metadata?.TryGetValue("userId", out var u) == true ? u : "missing")}");
Console.WriteLine($"Metadata planType: {(payment.Metadata?.TryGetValue("planType", out var p) == true ? p : "missing")}");
