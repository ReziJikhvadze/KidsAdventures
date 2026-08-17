namespace AdventurePacks.Api.DTOs.Portraits;

public sealed class PortraitCheckRequest
{
    /// <summary>The already-downscaled <c>data:image/…;base64,…</c> string held by the form.</summary>
    public string? PhotoDataUrl { get; set; }
}

public sealed class PortraitCheckResponse
{
    public bool Accepted { get; set; }

    /// <summary>One code from <c>PortraitGateReasons</c>; the browser localises from it.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Georgian, ready to show, for a caller with no copy of its own.</summary>
    public string Message { get; set; } = string.Empty;
}
