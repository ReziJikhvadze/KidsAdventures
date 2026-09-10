using System.Text.RegularExpressions;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Infrastructure;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Implementations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Adventrya.Story.Tests;

/// <summary>
/// The four digits that stand between a print order and the bank.
///
/// A printed book is posted to a phone number as much as to a street: the courier rings before
/// they climb the stairs, and a digit typed wrong is a parcel that comes back to us having cost
/// the printing, the postage and the disappointment. So checkout sends a code to the recipient
/// and will not take money until it comes back.
///
/// The tests below are about the two halves of that being true at once. The gate has to hold -
/// a client that skips the panel, a stale session, a guessed code - and it has to be asked once
/// and then never again, because a shop that keeps re-asking a question it already has the
/// answer to is a shop that has forgotten you.
/// </summary>
public sealed class RecipientPhoneVerificationTests
{
    private const string Recipient = "+995599123456";

    [Fact]
    public async Task A_number_nobody_proved_is_not_verified_and_stops_the_order()
    {
        var world = new World();

        var status = await world.Service.GetStatusAsync(world.Parent, Recipient, CancellationToken.None);
        Assert.False(status.Verified);

        // Masked on the way out: "is this number proved" must not double as a way to read the
        // number back off the server.
        Assert.DoesNotContain("123456", status.PhoneNumber, StringComparison.Ordinal);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Service.EnsureVerifiedAsync(world.Parent, Recipient, CancellationToken.None));
    }

    [Fact]
    public async Task The_code_that_was_sent_verifies_the_number_and_the_gate_opens()
    {
        var world = new World();

        await world.Service.RequestCodeAsync(world.Parent, Recipient, "127.0.0.1", CancellationToken.None);

        var (phone, message) = Assert.Single(world.Sms.Sent);
        Assert.Equal(Recipient, phone);

        // One segment. Georgian is UCS-2 to a gateway, so a message turns into a second charged
        // part after 70 characters.
        Assert.True(message.Length <= 70, $"The SMS is {message.Length} characters, so it is two.");

        // And it says what it is for. The handset receiving this is often not the parent's, and
        // "your code is 1234" with no subject reads as somebody trying to get into an account.
        Assert.Contains("მიწოდების", message, StringComparison.Ordinal);

        var status = await world.Service.VerifyAsync(
            world.Parent, Recipient, world.LastCode, CancellationToken.None);

        Assert.True(status.Verified);
        await world.Service.EnsureVerifiedAsync(world.Parent, Recipient, CancellationToken.None);
    }

    [Fact]
    public async Task A_wrong_code_is_refused_and_spends_a_guess()
    {
        var world = new World();
        await world.Service.RequestCodeAsync(world.Parent, Recipient, null, CancellationToken.None);

        var wrong = world.LastCode == "0000" ? "1111" : "0000";

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => world.Service.VerifyAsync(world.Parent, Recipient, wrong, CancellationToken.None));

        Assert.Equal(1, Assert.Single(world.Challenges.All).AttemptCount);

        // The budget is a budget, and the right code still works inside it.
        var status = await world.Service.VerifyAsync(
            world.Parent, Recipient, world.LastCode, CancellationToken.None);
        Assert.True(status.Verified);
    }

    [Fact]
    public async Task A_spent_code_cannot_be_used_twice()
    {
        var world = new World();
        await world.Service.RequestCodeAsync(world.Parent, Recipient, null, CancellationToken.None);
        var code = world.LastCode;

        await world.Service.VerifyAsync(world.Parent, Recipient, code, CancellationToken.None);

        // The number is remembered now, so this parent asking again is answered from the record
        // rather than from the spent challenge - which is the behaviour that matters. A second
        // parent replaying the same code is the case with teeth, and it is the next test.
        world.Verified.Clear();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => world.Service.VerifyAsync(world.Parent, Recipient, code, CancellationToken.None));
    }

    [Fact]
    public async Task One_parents_code_does_not_prove_the_number_for_another()
    {
        var world = new World();
        var neighbour = Guid.NewGuid();

        await world.Service.RequestCodeAsync(world.Parent, Recipient, null, CancellationToken.None);
        var code = world.LastCode;

        // Codes are issued against the parent as well as the number. Without that, the challenge
        // table is keyed by the number alone: one parent's code would verify for anybody who saw
        // it, and - worse the other way round - a stranger asking about the same number would
        // retire the code the first parent is holding, because issuing invalidates the pending
        // ones for a destination.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => world.Service.VerifyAsync(neighbour, Recipient, code, CancellationToken.None));

        Assert.False((await world.Service.GetStatusAsync(neighbour, Recipient, CancellationToken.None)).Verified);

        // The first parent's own code is still theirs.
        Assert.True((await world.Service.VerifyAsync(
            world.Parent, Recipient, code, CancellationToken.None)).Verified);
    }

    [Fact]
    public async Task A_number_already_proved_is_not_asked_about_again()
    {
        var world = new World();
        await world.Service.RequestCodeAsync(world.Parent, Recipient, null, CancellationToken.None);
        await world.Service.VerifyAsync(world.Parent, Recipient, world.LastCode, CancellationToken.None);

        world.Sms.Sent.Clear();

        // Not merely allowed through - refused. A recipient who gets a code for a book they were
        // told about last week has no idea what it is, and the panel would be asking a question
        // it already has the answer to.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Service.RequestCodeAsync(world.Parent, Recipient, null, CancellationToken.None));

        Assert.Empty(world.Sms.Sent);
        await world.Service.EnsureVerifiedAsync(world.Parent, Recipient, CancellationToken.None);
    }

    [Fact]
    public async Task The_parents_own_confirmed_number_counts_as_proved()
    {
        var world = new World();
        world.Users.PhoneNumber = Recipient;
        world.Users.PhoneConfirmed = true;

        // They signed in by reading four digits off this handset minutes ago. Asking them to do
        // it again is the shop failing to remember something it watched happen.
        Assert.True((await world.Service.GetStatusAsync(world.Parent, Recipient, CancellationToken.None)).Verified);
        await world.Service.EnsureVerifiedAsync(world.Parent, Recipient, CancellationToken.None);
        Assert.Empty(world.Sms.Sent);
    }

    [Fact]
    public async Task An_unconfirmed_number_on_the_account_proves_nothing()
    {
        var world = new World();
        world.Users.PhoneNumber = Recipient;
        world.Users.PhoneConfirmed = false;

        // A number typed into a profile is a claim; a number that answered a code is a fact.
        Assert.False((await world.Service.GetStatusAsync(world.Parent, Recipient, CancellationToken.None)).Verified);
    }

    [Fact]
    public async Task The_same_number_written_two_ways_is_one_number()
    {
        var world = new World();
        await world.Service.RequestCodeAsync(world.Parent, "599 12 34 56", null, CancellationToken.None);
        await world.Service.VerifyAsync(world.Parent, "+995 599 12 34 56", world.LastCode, CancellationToken.None);

        // Typed with spaces on Monday and without them on Friday. Normalising on the way in is
        // what keeps that from becoming a second number to prove.
        await world.Service.EnsureVerifiedAsync(world.Parent, "995599123456", CancellationToken.None);
    }

    [Fact]
    public async Task Asking_again_too_soon_is_refused_with_the_wait()
    {
        var world = new World();
        await world.Service.RequestCodeAsync(world.Parent, Recipient, null, CancellationToken.None);

        var refusal = await Assert.ThrowsAsync<TooManyRequestsException>(
            () => world.Service.RequestCodeAsync(world.Parent, Recipient, null, CancellationToken.None));

        Assert.InRange(refusal.RetryAfterSeconds, 1, 45);
        Assert.Single(world.Sms.Sent);
    }

    /// <summary>
    /// Every purpose the code can write is a purpose the database will accept.
    ///
    /// The one failure the in-memory tests above cannot see, and the one that would have shipped:
    /// <c>AuthChallenges.Purpose</c> is written as text and guarded by a CHECK naming the
    /// purposes that existed when the table was created. Adding a third to the enum is half the
    /// change - without the other half every code request throws a constraint violation the
    /// moment it reaches SQL, and nothing before that point notices, because the repository is
    /// perfectly happy to hand the database a string it will refuse.
    ///
    /// Read out of the migrations rather than restated here, so a fourth purpose has one place to
    /// be wrong and this test is the thing that says so.
    /// </summary>
    [Fact]
    public void Every_challenge_purpose_is_one_the_database_accepts()
    {
        var scripts = Path.Combine(AppContext.BaseDirectory, "Data", "Scripts");
        Assert.True(Directory.Exists(scripts), $"No SQL scripts beside the tests at {scripts}.");

        // The last script that rewrites the constraint is the one in force.
        var constraint = Directory.GetFiles(scripts, "*.sql")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText)
            .Select(sql => Regex.Match(
                sql,
                @"CONSTRAINT\s+CK_AuthChallenges_Purpose\s*CHECK\s*\(\s*Purpose\s+IN\s*\(([^)]*)\)"))
            .LastOrDefault(match => match.Success);

        Assert.True(constraint is { Success: true }, "No CK_AuthChallenges_Purpose in any migration.");

        var permitted = constraint!.Groups[1].Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.Trim().TrimStart('N').Trim('\''))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var purpose in Enum.GetNames<AuthChallengePurpose>())
        {
            Assert.True(
                permitted.Contains(purpose),
                $"AuthChallengePurpose.{purpose} is written to the database as '{purpose}', which "
                + $"CK_AuthChallenges_Purpose does not permit. It allows: {string.Join(", ", permitted)}.");
        }
    }

    // -- the world ---------------------------------------------------------

    /// <summary>
    /// Everything the service talks to, in memory, with the code it issued readable afterwards.
    ///
    /// The code is read out of the SMS rather than out of the challenge row, because the row only
    /// holds an HMAC of it - which is the property worth keeping, so the test reads it the way a
    /// recipient does.
    /// </summary>
    private sealed class World
    {
        public Guid Parent { get; } = Guid.NewGuid();

        public InMemoryChallenges Challenges { get; } = new();

        public InMemoryVerifiedPhones Verified { get; } = new();

        public SingleUserRepository Users { get; } = new();

        public RecordingSms Sms { get; } = new();

        public string LastCode =>
            new(Sms.Sent[^1].Message.Where(char.IsAsciiDigit).ToArray());

        public RecipientPhoneVerificationService Service { get; }

        public World()
        {
            var options = Options.Create(new PasswordlessAuthOptions
            {
                // Off, so a test cannot pass by reading a secret the production configuration
                // would never return.
                ExposeSecretsInResponse = false
            });

            var codes = new OneTimeCodeService(
                Challenges,
                options,
                Options.Create(new JwtOptions { SecretKey = new string('k', 64) }),
                NullLogger<OneTimeCodeService>.Instance);

            Service = new RecipientPhoneVerificationService(
                codes,
                Verified,
                Users,
                Sms,
                options,
                NullLogger<RecipientPhoneVerificationService>.Instance);
        }
    }

    private sealed class InMemoryVerifiedPhones : IVerifiedRecipientPhoneRepository
    {
        private readonly HashSet<(Guid User, string Phone)> _rows = [];

        public void Clear() => _rows.Clear();

        public Task<bool> IsVerifiedAsync(Guid userId, string phoneNumber, CancellationToken ct) =>
            Task.FromResult(_rows.Contains((userId, phoneNumber)));

        public Task MarkVerifiedAsync(Guid userId, string phoneNumber, CancellationToken ct)
        {
            _rows.Add((userId, phoneNumber));
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryChallenges : IAuthChallengeRepository
    {
        private readonly List<AuthChallenge> _rows = [];

        public IReadOnlyList<AuthChallenge> All => _rows;

        public Task InsertAsync(AuthChallenge challenge, CancellationToken ct)
        {
            _rows.Add(challenge);
            return Task.CompletedTask;
        }

        public Task<AuthChallenge?> GetByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(_rows.FirstOrDefault(row => row.Id == id));

        public Task<AuthChallenge?> GetLatestAsync(AuthChallengePurpose purpose, string destination, CancellationToken ct) =>
            Task.FromResult(Matching(purpose, destination).OrderByDescending(row => row.CreatedAt).FirstOrDefault());

        public Task<AuthChallenge?> GetLatestPendingAsync(AuthChallengePurpose purpose, string destination, CancellationToken ct) =>
            Task.FromResult(Matching(purpose, destination)
                .Where(row => row.IsPending(DateTime.UtcNow))
                .OrderByDescending(row => row.CreatedAt)
                .FirstOrDefault());

        public Task<int> CountByDestinationSinceAsync(AuthChallengePurpose purpose, string destination, DateTime since, CancellationToken ct) =>
            Task.FromResult(Matching(purpose, destination).Count(row => row.CreatedAt >= since));

        public Task<int> CountByIpSinceAsync(string ipAddress, DateTime since, CancellationToken ct) =>
            Task.FromResult(_rows.Count(row => row.IpAddress == ipAddress && row.CreatedAt >= since));

        public Task<bool> TryConsumeAsync(Guid id, CancellationToken ct)
        {
            var row = _rows.FirstOrDefault(candidate => candidate.Id == id);
            if (row is null || row.ConsumedAt is not null) return Task.FromResult(false);

            row.ConsumedAt = DateTime.UtcNow;
            return Task.FromResult(true);
        }

        public Task<int> RecordFailedAttemptAsync(Guid id, CancellationToken ct)
        {
            var row = _rows.First(candidate => candidate.Id == id);
            row.AttemptCount++;
            return Task.FromResult(row.AttemptCount);
        }

        public Task InvalidatePendingAsync(AuthChallengePurpose purpose, string destination, CancellationToken ct)
        {
            foreach (var row in Matching(purpose, destination).Where(row => row.ConsumedAt is null))
            {
                row.ConsumedAt = DateTime.UtcNow;
            }

            return Task.CompletedTask;
        }

        public Task<int> DeleteExpiredAsync(DateTime olderThanUtc, CancellationToken ct) =>
            throw new NotSupportedException();

        private IEnumerable<AuthChallenge> Matching(AuthChallengePurpose purpose, string destination) =>
            _rows.Where(row => row.Purpose == purpose
                               && string.Equals(row.Destination, destination, StringComparison.Ordinal));
    }
}
