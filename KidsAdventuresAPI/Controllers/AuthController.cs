using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.DTOs.Auth;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IAuthService authService,
    IPasswordlessAuthService passwordlessAuthService,
    IUserContextService userContext,
    IUserRepository userRepository,
    IOptions<EmailOptions> emailOptions,
    IOptions<GoogleAuthOptions> googleAuthOptions,
    IOptions<RecaptchaOptions> recaptchaOptions) : ControllerBase
{
    [HttpGet("config")]
    [AllowAnonymous]
    public ActionResult<AuthConfigResponse> GetConfig()
    {
        var google = googleAuthOptions.Value;
        var enabled = google.Enabled && !string.IsNullOrWhiteSpace(google.ClientId);

        var recaptcha = recaptchaOptions.Value;
        var recaptchaEnabled = recaptcha.Enabled && !string.IsNullOrWhiteSpace(recaptcha.SiteKey);

        return Ok(new AuthConfigResponse
        {
            GoogleEnabled = enabled,
            GoogleClientId = enabled ? google.ClientId : null,
            RecaptchaEnabled = recaptchaEnabled,
            RecaptchaSiteKey = recaptchaEnabled ? recaptcha.SiteKey : null,
            Passwordless = passwordlessAuthService.GetConfig()
        });
    }

    /// <summary>
    /// Emails a one-time sign-in link. Responds identically whether or not the address has an
    /// account: the account is created when the link is verified, so there is nothing to leak.
    /// </summary>
    [HttpPost("magic-link")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthChallengeResponse>> RequestMagicLink(
        [FromBody] MagicLinkRequest request,
        CancellationToken cancellationToken)
    {
        var response = await passwordlessAuthService.RequestMagicLinkAsync(request, ClientIp(), cancellationToken);
        return Ok(response);
    }

    [HttpPost("magic-link/verify")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> VerifyMagicLink(
        [FromBody] VerifyMagicLinkRequest request,
        CancellationToken cancellationToken)
    {
        var response = await passwordlessAuthService.VerifyMagicLinkAsync(request, cancellationToken);
        return Ok(response);
    }

    /// <summary>Sends a six-digit code to a Georgian mobile number.</summary>
    [HttpPost("phone/code")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthChallengeResponse>> RequestPhoneCode(
        [FromBody] PhoneCodeRequest request,
        CancellationToken cancellationToken)
    {
        var response = await passwordlessAuthService.RequestPhoneCodeAsync(request, ClientIp(), cancellationToken);
        return Ok(response);
    }

    [HttpPost("phone/verify")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> VerifyPhoneCode(
        [FromBody] VerifyPhoneCodeRequest request,
        CancellationToken cancellationToken)
    {
        var response = await passwordlessAuthService.VerifyPhoneCodeAsync(request, cancellationToken);
        return Ok(response);
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var response = await authService.RegisterAsync(request, cancellationToken);
        return Ok(response);
    }

    [HttpPost("continue")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Continue([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var response = await authService.ContinueAsync(request, cancellationToken);
        return Ok(response);
    }

    [HttpPost("email-status")]
    [AllowAnonymous]
    public async Task<ActionResult<EmailStatusResponse>> EmailStatus(
        [FromBody] EmailStatusRequest request,
        CancellationToken cancellationToken)
    {
        var response = await authService.GetEmailStatusAsync(request.Email, cancellationToken);
        return Ok(response);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var response = await authService.LoginAsync(request, cancellationToken);
        return Ok(response);
    }

    [HttpPost("google")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> LoginWithGoogle(
        [FromBody] GoogleLoginRequest request,
        CancellationToken cancellationToken)
    {
        var response = await authService.LoginWithGoogleAsync(request, cancellationToken);
        return Ok(response);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<SessionInfoResponse>> GetSession(CancellationToken cancellationToken)
    {
        var userId = userContext.GetUserId();
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);

        return Ok(new SessionInfoResponse
        {
            Email = user?.Email ?? userContext.GetEmail(),
            PhoneNumber = user?.PhoneNumber,
            DisplayName = user?.DisplayName,
            PreferredLanguage = user?.PreferredLanguage ?? "ka",
            IsAdmin = user?.IsAdmin ?? false,
            WelcomeStoryRemaining = user?.WelcomeStoryRemaining ?? 0
        });
    }

    [HttpPost("confirm-email")]
    [AllowAnonymous]
    public async Task<ActionResult<ConfirmEmailResponse>> ConfirmEmailPost(
        [FromBody] ConfirmEmailRequest request,
        CancellationToken cancellationToken)
    {
        var success = await authService.ConfirmEmailAsync(request.Token, cancellationToken);
        return Ok(new ConfirmEmailResponse
        {
            Success = success,
            Message = success
                ? "Your email is confirmed. You can sign in now."
                : "This confirmation link is invalid or has expired."
        });
    }

    [HttpGet("confirm-email")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmEmail([FromQuery] string token, CancellationToken cancellationToken)
    {
        var success = await authService.ConfirmEmailAsync(token, cancellationToken);
        var baseUrl = emailOptions.Value.BaseUrl.TrimEnd('/');
        var redirect = success
            ? $"{baseUrl}/confirm-email?success=1"
            : $"{baseUrl}/confirm-email?success=0";
        return Redirect(redirect);
    }
}
