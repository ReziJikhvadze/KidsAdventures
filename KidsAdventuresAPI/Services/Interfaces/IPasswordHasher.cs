namespace AdventurePacks.Api.Services.Interfaces;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string passwordHash);
}
