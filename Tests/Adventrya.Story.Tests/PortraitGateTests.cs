using AdventurePacks.Api.Services.Beki;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adventrya.Story.Tests;

/// <summary>
/// The gate's rules, without the model.
///
/// Every case here is one a live call cannot be relied on to produce on demand — a timeout, an
/// answer that does not parse, a refusal code from a newer prompt — and each has the same failure
/// mode if it is wrong: a photo that should have been refused is accepted, and the mistake is only
/// discovered at the end of a generated book.
/// </summary>
public class PortraitGateTests
{
    [Fact]
    public void An_accepted_photo_passes_with_no_message()
    {
        var verdict = PortraitGate.Interpret(
            new PortraitGateResponse { Accepted = true, Reason = "ok", Explanation = "A child facing the camera." },
            NullLogger.Instance);

        Assert.True(verdict.Accepted);
        Assert.Equal(PortraitGateReasons.Ok, verdict.Reason);
    }

    [Theory]
    [InlineData(PortraitGateReasons.NotAPerson)]
    [InlineData(PortraitGateReasons.NoFace)]
    [InlineData(PortraitGateReasons.MultiplePeople)]
    [InlineData(PortraitGateReasons.FaceObscured)]
    [InlineData(PortraitGateReasons.FaceTooSmall)]
    [InlineData(PortraitGateReasons.TooDark)]
    public void A_refusal_keeps_its_reason_and_carries_wording(string reason)
    {
        var verdict = PortraitGate.Interpret(
            new PortraitGateResponse { Accepted = false, Reason = reason },
            NullLogger.Instance);

        Assert.False(verdict.Accepted);
        Assert.Equal(reason, verdict.Reason);

        // Every code the model may return has to have copy, or a parent is refused in silence.
        Assert.False(string.IsNullOrWhiteSpace(verdict.Message));
        Assert.NotEqual(PortraitGateReasons.MessageFor(PortraitGateReasons.Unsuitable), verdict.Message);
    }

    [Fact]
    public void A_refusal_with_an_unknown_code_still_says_something_useful()
    {
        // A later prompt revision inventing a code must not reach the parent as a blank message.
        var verdict = PortraitGate.Interpret(
            new PortraitGateResponse { Accepted = false, Reason = "wearing_a_hat" },
            NullLogger.Instance);

        Assert.False(verdict.Accepted);
        Assert.Equal(PortraitGateReasons.Unsuitable, verdict.Reason);
        Assert.False(string.IsNullOrWhiteSpace(verdict.Message));
    }

    [Fact]
    public void An_answer_that_did_not_parse_lets_the_photo_through()
    {
        /*
          Null is what the client returns when the model produced nothing usable — a timeout, an
          unreachable endpoint, an answer that would not deserialize.

          This used to be a refusal, on the reasoning that a gate unable to judge should not open.
          In practice it inverted the product: none of those are facts about the photograph, and a
          parent whose photo was perfectly good was told, in Georgian, that it would not do. The
          check is a courtesy that catches somebody who picked a bottle by mistake; when it cannot
          run there is nothing to say, and nothing to say means carry on.
        */
        var verdict = PortraitGate.Interpret(null, NullLogger.Instance);

        Assert.True(verdict.Accepted);
        Assert.Equal(PortraitGateReasons.Ok, verdict.Reason);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public void The_three_formats_the_picker_accepts_decode(string contentType)
    {
        var dataUrl = $"data:{contentType};base64,{Convert.ToBase64String([1, 2, 3, 4])}";

        Assert.True(PortraitDataUrl.TryDecode(dataUrl, out var bytes, out var decodedType));
        Assert.Equal(contentType, decodedType);
        Assert.Equal(4, bytes.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-data-url")]
    [InlineData("data:image/jpeg;base64,")]                    // header only, no payload
    [InlineData("data:image/jpeg,hello")]                      // not base64
    [InlineData("data:text/html;base64,PGh0bWw+")]             // not an image at all
    [InlineData("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=")] // scriptable, and never from the picker
    [InlineData("data:image/jpeg;base64,!!!!")]                // undecodable payload
    public void Anything_that_is_not_one_of_those_is_refused(string? dataUrl)
    {
        Assert.False(PortraitDataUrl.TryDecode(dataUrl, out _, out _));
    }
}
