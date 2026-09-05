namespace AdventurePacks.Api.Configuration.Options;

/// <summary>
/// The browser key for the Maps JavaScript API, used by the delivery address picker.
///
/// Separate from <see cref="GoogleAuthOptions"/> even though both are Google and both are public,
/// because they are different credentials with different blast radii: sign-in breaks if the client
/// id is wrong, and the map quietly stops finding addresses if this one is. Keeping them apart
/// means either can be rotated without touching the other.
///
/// This key reaches the browser by design — the Maps script cannot be called any other way — so it
/// is not a secret in the sense the SMTP password is. It still must be restricted in Google Cloud
/// to Beki's own hosts, because an unrestricted browser key is a bill anybody can run up.
///
/// Absent or empty, the picker is not offered at all and the address is typed as it always was.
/// </summary>
public sealed class GoogleMapsOptions
{
    public const string SectionName = "GoogleMaps";

    public bool Enabled { get; set; }

    public string ApiKey { get; set; } = string.Empty;
}
