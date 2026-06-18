namespace AdventurePacks.Api.Services.Interfaces;

public interface IRecaptchaVerifier
{
    /// <summary>True when reCAPTCHA is enabled and configured.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Verifies a client token. Returns true immediately when reCAPTCHA is disabled (scaffold mode),
    /// otherwise validates the token against Google's siteverify endpoint.
    /// </summary>
    Task<bool> VerifyAsync(string? token, CancellationToken cancellationToken);
}
