using System.ComponentModel.DataAnnotations;

namespace AdventurePacks.Api.DTOs.Contact;

public sealed class ContactRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(200)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string Message { get; set; } = string.Empty;

    /// <summary>Honeypot — must stay empty.</summary>
    [MaxLength(200)]
    public string? Company { get; set; }
}

public sealed class ContactResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
