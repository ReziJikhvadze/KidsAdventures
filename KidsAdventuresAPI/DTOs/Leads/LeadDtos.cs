using System.ComponentModel.DataAnnotations;

namespace AdventurePacks.Api.DTOs.Leads;

public sealed class CaptureLeadRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? Source { get; set; }

    [MaxLength(128)]
    public string? ChildName { get; set; }

    [MaxLength(64)]
    public string? Theme { get; set; }

    /// <summary>Honeypot — must stay empty.</summary>
    [MaxLength(200)]
    public string? Company { get; set; }
}

public sealed class CaptureLeadResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
