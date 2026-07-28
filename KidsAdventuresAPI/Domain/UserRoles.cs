namespace AdventurePacks.Api.Domain;

public static class UserRoles
{
    /// <summary>Gates the print-fulfilment console. Granted by <c>Users.IsAdmin</c>.</summary>
    public const string Admin = "Admin";
}

public static class AuthorizationPolicies
{
    public const string Admin = "AdminOnly";
}
