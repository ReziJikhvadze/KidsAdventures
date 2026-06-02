namespace AdventurePacks.Api.Domain.Entities;

public sealed class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public SubscriptionType SubscriptionType { get; set; } = SubscriptionType.Free;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
