namespace AdventurePacks.Api.Services.Interfaces;

public interface IUserContextService
{
    Guid GetUserId();
    string GetEmail();
}
