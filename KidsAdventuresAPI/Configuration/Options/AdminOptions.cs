namespace AdventurePacks.Api.Configuration.Options;

/// <summary>
/// The accounts that hold the operations role no matter what the database says.
///
/// The role itself lives in <c>Users.IsAdmin</c> and is granted from the console, which is
/// the ordinary path. This list exists for the two cases that path cannot serve: the first
/// admin, before anyone exists to grant it, and the mistake — a demotion, a bad restore, a
/// migration that wrote the column wrong — that would otherwise leave the console with no
/// way back in. An address here is always an admin and can never be demoted.
///
/// Empty by default. An installation that sets nothing behaves exactly as it did before.
/// </summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    public string[] SuperAdminEmails { get; set; } = [];

    /// <summary>Case- and whitespace-insensitive: this is typed into a portal by hand.</summary>
    public bool IsSuperAdmin(string? email) =>
        !string.IsNullOrWhiteSpace(email)
        && SuperAdminEmails.Any(configured =>
            string.Equals(configured?.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));
}
