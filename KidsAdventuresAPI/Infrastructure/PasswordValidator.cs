namespace AdventurePacks.Api.Infrastructure;

public static class PasswordValidator
{
    public static void ValidateOrThrow(string password)
    {
        if (password.Length < 8)
        {
            throw new InvalidOperationException("Password must be at least 8 characters.");
        }

        if (!password.Any(char.IsDigit))
        {
            throw new InvalidOperationException("Password must contain at least one digit.");
        }

        if (!password.Any(char.IsUpper))
        {
            throw new InvalidOperationException("Password must contain at least one uppercase letter.");
        }

        if (!password.Any(char.IsLower))
        {
            throw new InvalidOperationException("Password must contain at least one lowercase letter.");
        }
    }
}
