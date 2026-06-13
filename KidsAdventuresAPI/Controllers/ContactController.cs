using AdventurePacks.Api.DTOs.Contact;
using AdventurePacks.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AdventurePacks.Api.Controllers;

[ApiController]
[Route("api/contact")]
public sealed class ContactController(IEmailService emailService) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<ContactResponse>> Submit(
        [FromBody] ContactRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new ContactResponse
            {
                Success = false,
                Message = "Please check your name, email, and message.",
            });
        }

        if (!string.IsNullOrWhiteSpace(request.Company))
        {
            return Ok(new ContactResponse
            {
                Success = true,
                Message = "Thanks — we'll get back to you soon.",
            });
        }

        try
        {
            await emailService.SendContactFormAsync(
                request.Name.Trim(),
                request.Email.Trim(),
                request.Message.Trim(),
                cancellationToken);

            return Ok(new ContactResponse
            {
                Success = true,
                Message = "Thanks — your message was sent. We'll reply by email soon.",
            });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ContactResponse
            {
                Success = false,
                Message = ex.Message,
            });
        }
    }
}
