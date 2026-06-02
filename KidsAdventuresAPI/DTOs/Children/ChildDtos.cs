namespace AdventurePacks.Api.DTOs.Children;

public sealed class CreateChildRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Range(1, 18)]
    public int Age { get; set; }
}

public sealed class UpdateChildRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Range(1, 18)]
    public int Age { get; set; }
}

public sealed class ChildResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public DateTime CreatedAt { get; set; }
}
