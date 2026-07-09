using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.DTOs.Leads;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Controllers;

[ApiController]
[Route("api/leads")]
public sealed class LeadsController(
    ILeadRepository leadRepository,
    IEmailService emailService,
    IOptions<EmailOptions> emailOptions,
    ILogger<LeadsController> logger) : ControllerBase
{
    private readonly EmailOptions _emailOptions = emailOptions.Value;

    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<CaptureLeadResponse>> Capture(
        [FromBody] CaptureLeadRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new CaptureLeadResponse
            {
                Success = false,
                Message = "Please enter a valid email address.",
            });
        }

        // Honeypot: bots fill hidden fields. Pretend success so we don't tip them off.
        if (!string.IsNullOrWhiteSpace(request.Company))
        {
            return Ok(new CaptureLeadResponse { Success = true, Message = "Thanks — check your inbox!" });
        }

        var lead = new Lead
        {
            Email = request.Email.Trim(),
            Source = string.IsNullOrWhiteSpace(request.Source) ? "exit-intent" : request.Source.Trim(),
            ChildName = string.IsNullOrWhiteSpace(request.ChildName) ? null : request.ChildName.Trim(),
            Theme = string.IsNullOrWhiteSpace(request.Theme) ? null : request.Theme.Trim(),
        };

        var isNew = await leadRepository.TryCreateAsync(lead, cancellationToken);

        // Only email a brand-new lead, and never let an SMTP hiccup fail the capture.
        if (isNew)
        {
            try
            {
                var ctaUrl = $"{_emailOptions.BaseUrl.TrimEnd('/')}/";
                await emailService.SendLeadMagnetAsync(lead.Email, lead.ChildName, ctaUrl, cancellationToken);
                await leadRepository.MarkEmailedAsync(lead.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Captured lead {Email} but could not send the welcome email.", lead.Email);
            }
        }

        return Ok(new CaptureLeadResponse
        {
            Success = true,
            Message = "Thanks — your free storybook link is on its way to your inbox!",
        });
    }
}
