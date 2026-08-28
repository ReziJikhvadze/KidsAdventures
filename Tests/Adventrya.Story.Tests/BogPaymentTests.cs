using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Implementations;
using AdventurePacks.Api.Services.Interfaces;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Adventrya.Story.Tests;

/// <summary>
/// The two things about BOG that cannot be checked by reading the code: that we read the
/// gateway's payloads the way it actually writes them, and that a forged callback is refused.
///
/// The receipt below is a real response from BOG's sandbox, kept verbatim. It is the fixture
/// precisely because a hand-written one would agree with whatever the parser expects.
/// </summary>
public class BogPaymentTests
{
    private const string SandboxReceiptJson =
        """
        {
          "order_id": "9a4b2f87-3253-48f0-9200-82acd11e7964",
          "external_order_id": "8a9f241d-5296-4891-bc76-e8b5fa420d56",
          "industry": "ecommerce",
          "capture": "automatic",
          "order_status": { "key": "created", "value": "Created" },
          "payment_detail": {
            "transfer_method": { "key": "", "value": "" },
            "transaction_id": null,
            "payer_identifier": null,
            "payment_option": "direct_debit",
            "code": null
          },
          "purchase_units": {
            "request_amount": "14.0",
            "transfer_amount": "0.0",
            "currency_code": "GEL"
          }
        }
        """;

    [Fact]
    public void An_unpaid_receipt_is_read_as_neither_paid_nor_failed()
    {
        var details = Parse(SandboxReceiptJson);

        Assert.NotNull(details);
        Assert.Equal("9a4b2f87-3253-48f0-9200-82acd11e7964", details.BogOrderId);
        Assert.Equal(Guid.Parse("8a9f241d-5296-4891-bc76-e8b5fa420d56"), details.OrderId);
        Assert.Equal("created", details.StatusKey);
        Assert.Null(details.TransactionId);

        // "created" is the state a payment page sits in before anyone touches it. Treating it
        // as either outcome would fulfil an unpaid book or fail a live one.
        Assert.False(details.IsPaid);
        Assert.False(details.IsFailed);
    }

    [Fact]
    public void A_completed_callback_carries_the_order_and_the_transaction()
    {
        var details = Parse(
            """
            {
              "order_id": "9a4b2f87-3253-48f0-9200-82acd11e7964",
              "external_order_id": "8a9f241d-5296-4891-bc76-e8b5fa420d56",
              "order_status": { "key": "completed", "value": "Completed" },
              "payment_detail": { "transaction_id": "24080100123456", "code": "100" }
            }
            """);

        Assert.NotNull(details);
        Assert.True(details.IsPaid);
        Assert.Equal("24080100123456", details.TransactionId);
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("blocked")]
    public void A_terminal_status_fails_the_order(string statusKey)
    {
        var details = Parse($$"""
            {
              "order_id": "9a4b2f87-3253-48f0-9200-82acd11e7964",
              "order_status": { "key": "{{statusKey}}", "value": "x" }
            }
            """);

        Assert.NotNull(details);
        Assert.True(details.IsFailed);
        Assert.False(details.IsPaid);
    }

    [Fact]
    public void A_payload_with_no_order_id_is_not_actionable()
    {
        Assert.Null(Parse("""{ "event": "order_payment" }"""));
    }

    [Fact]
    public void A_callback_signed_by_anyone_but_the_bank_is_refused()
    {
        var body = Encoding.UTF8.GetBytes(SandboxReceiptJson);

        // A well-formed signature over the exact bytes — from the wrong key. If the pinned
        // public key were not doing real work, this is what would slip through.
        using var impostor = RSA.Create(2048);
        var signature = Convert.ToBase64String(
            impostor.SignData(body, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        Assert.False(Client().VerifyCallbackSignature(body, signature));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-base64!!")]
    public void A_missing_or_unreadable_signature_is_refused(string? signature)
    {
        var body = Encoding.UTF8.GetBytes(SandboxReceiptJson);

        Assert.False(Client().VerifyCallbackSignature(body, signature));
    }

    private static BogPaymentDetails? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return BogPaymentClient.ParseDetails(document.RootElement);
    }

    private static IBogPaymentClient Client() => new BogPaymentClient(
        new NullHttpClientFactory(),
        Options.Create(new BogOptions()),
        NullLogger<BogPaymentClient>.Instance);

    /// <summary>Signature checking never leaves the process, so nothing here needs a real client.</summary>
    private sealed class NullHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
