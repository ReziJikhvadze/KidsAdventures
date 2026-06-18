using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.DTOs.Auth;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IAuthService authService,
    ISubscriptionService subscriptionService,
    IUserContextService userContext,
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
        });
    }

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
        var balance = await subscriptionService.GetAccountBalanceAsync(userContext.GetUserId(), cancellationToken);
        return Ok(new SessionInfoResponse
        {
            Email = userContext.GetEmail(),
            BookCredits = balance.BookCredits,
            StoriesUsedThisMonth = balance.StoriesUsedThisMonth,
            StoriesAllowedThisMonth = balance.StoriesAllowedThisMonth,
            StoriesRemainingThisMonth = balance.StoriesRemainingThisMonth,
            WelcomeStoryRemaining = balance.WelcomeStoryRemaining,
            SubscriptionType = balance.SubscriptionType,
            HasUnlimitedPdf = false
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
