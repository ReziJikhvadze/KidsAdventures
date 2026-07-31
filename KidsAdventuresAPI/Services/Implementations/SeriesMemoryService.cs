using System.Text;
using System.Text.Json;

using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Infrastructure;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

/// <summary>
/// Keeps one distilled memory per series so book N can genuinely follow books 1..N-1.
///
/// The distillation is a single cheap text call: previous snapshot + the book just written in,
/// one merged snapshot out. That keeps the input to the *story* model at a fixed small size no
/// matter how long the series runs, which is the difference between a series that stays coherent
/// at book ten and one that becomes unaffordable at book four.
///
/// Everything here is best-effort. A child's book must never fail because their memory could not
/// be summarised, so both entry points swallow their errors and log.
/// </summary>
public sealed class SeriesMemoryService(
    ISeriesMemoryRepository seriesMemoryRepository,
    IOpenAiService openAiService,
    ILogger<SeriesMemoryService> logger) : ISeriesMemoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Enough for callbacks to feel rich; small enough that the prompt stays cheap.</summary>
    private const int MaxCompanions = 6;
    private const int MaxMemories = 8;
    private const int MaxTraits = 6;

    public async Task<string?> GetPromptMemoryAsync(Guid seriesId, CancellationToken cancellationToken)
    {
        if (seriesId == Guid.Empty)
        {
            return null;
        }

        try
        {
            var stored = await seriesMemoryRepository.GetBySeriesIdAsync(seriesId, cancellationToken);
            if (stored is null)
            {
                return null;
            }

            // MemoryText is written at distillation time in the book's language; the JSON is the
            // fallback for rows written before a render existed.
            if (!string.IsNullOrWhiteSpace(stored.MemoryText))
            {
                return stored.MemoryText;
            }

            var snapshot = Deserialize(stored.MemoryJson);
            return snapshot is null ? null : Render(snapshot);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reading series memory for {SeriesId} failed; writing without it.", seriesId);
            return null;
        }
    }

    public async Task RecordBookAsync(
        AdventurePack book,
        string storyJson,
        string heroName,
        CancellationToken cancellationToken)
    {
        if (book.SeriesId is not { } seriesId || seriesId == Guid.Empty || string.IsNullOrWhiteSpace(storyJson))
        {
            return;
        }

        try
        {
            var existing = await seriesMemoryRepository.GetBySeriesIdAsync(seriesId, cancellationToken);

            // Story generation can be re-run for the same book (retry, sweeper). Folding it in
            // twice would duplicate every companion it introduced.
            if (existing?.LastBookId == book.Id)
            {
                return;
            }

            var previous = existing is null ? null : Deserialize(existing.MemoryJson);
            var language = string.IsNullOrWhiteSpace(book.StoryLanguage) ? "ka" : book.StoryLanguage;

            var prompt = BuildDistillPrompt(previous, storyJson, book.WorldId, language, heroName);
            var raw = await openAiService.CompleteTextAsync(prompt, cancellationToken);

            var merged = Deserialize(ModelJsonSanitizer.ExtractJsonObject(raw));
            if (merged is null)
            {
                logger.LogWarning("Series memory distillation for book {BookId} returned unusable JSON.", book.Id);
                return;
            }

            Trim(merged);

            await seriesMemoryRepository.UpsertAsync(
                new SeriesMemory
                {
                    SeriesId = seriesId,
                    UserId = book.UserId,
                    MemoryJson = JsonSerializer.Serialize(merged, JsonOptions),
                    MemoryText = Render(merged),
                    LastBookId = book.Id,
                    BookCount = (existing?.BookCount ?? 0) + 1
                },
                cancellationToken);

            logger.LogInformation(
                "Series memory updated for series {SeriesId} after book {BookId} ({Companions} companions, {Memories} memories).",
                seriesId, book.Id, merged.Companions.Count, merged.Memories.Count);
        }
        catch (Exception ex)
        {
            // The book is already written and paid for. Losing its memory degrades the next
            // book; failing here would lose this one.
            logger.LogWarning(ex, "Distilling series memory for book {BookId} failed.", book.Id);
        }
    }

    private static SeriesMemorySnapshot? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SeriesMemorySnapshot>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Trim(SeriesMemorySnapshot snapshot)
    {
        // Newest first, so the cap drops the oldest details rather than the freshest ones.
        snapshot.Companions = snapshot.Companions
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .DistinctBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Take(MaxCompanions)
            .ToList();

        snapshot.Memories = snapshot.Memories
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxMemories)
            .ToList();

        snapshot.HeroTraits = snapshot.HeroTraits
            .Where(trait => !string.IsNullOrWhiteSpace(trait))
            .Select(trait => trait.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTraits)
            .ToList();

        snapshot.Worlds = snapshot.Worlds
            .Where(w => !string.IsNullOrWhiteSpace(w.WorldId))
            .DistinctBy(w => w.WorldId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Flattens the snapshot into the block the story prompt reads.</summary>
    private static string Render(SeriesMemorySnapshot snapshot)
    {
        var builder = new StringBuilder();

        if (snapshot.Companions.Count > 0)
        {
            builder.AppendLine("Companions already in this child's world (reuse them, do not reinvent them):");
            foreach (var companion in snapshot.Companions)
            {
                var description = string.IsNullOrWhiteSpace(companion.Description)
                    ? string.Empty
                    : $" — {companion.Description.Trim()}";
                var met = string.IsNullOrWhiteSpace(companion.MetIn)
                    ? string.Empty
                    : $" (met: {companion.MetIn.Trim()})";
                builder.AppendLine($"- {companion.Name.Trim()}{description}{met}");
            }
        }

        if (snapshot.Memories.Count > 0)
        {
            builder.AppendLine("Moments the child remembers from earlier books:");
            foreach (var memory in snapshot.Memories)
            {
                builder.AppendLine($"- {memory}");
            }
        }

        if (snapshot.HeroTraits.Count > 0)
        {
            builder.AppendLine($"Established about the hero: {string.Join(", ", snapshot.HeroTraits)}.");
        }

        if (snapshot.Worlds.Count > 0)
        {
            builder.AppendLine("Places already visited:");
            foreach (var world in snapshot.Worlds)
            {
                var state = string.IsNullOrWhiteSpace(world.LeftAs) ? "visited" : world.LeftAs.Trim();
                builder.AppendLine($"- {world.WorldId}: {state}");
            }
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Goal))
        {
            builder.AppendLine($"The thread running through the series: {snapshot.Goal.Trim()}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildDistillPrompt(
        SeriesMemorySnapshot? previous,
        string storyJson,
        string? worldId,
        string language,
        string heroName)
    {
        var previousJson = previous is null
            ? "{}"
            : JsonSerializer.Serialize(previous, JsonOptions);

        return $$"""
            You maintain the running memory of a children's book series.

            THE HERO OF THIS SERIES IS: {{heroName}}
            {{heroName}} is the child the books are written for. Everyone else — friends, animals,
            guides — is a companion, never the hero.

            Each book visits a world and introduces people, places and moments that later books
            should be able to call back to. Merge the NEW BOOK into the EXISTING MEMORY and return
            the merged memory.

            Rules:
            - Return RAW JSON only. No markdown, no code fences, no commentary.
            - Keep every companion that still matters; add the ones this book introduced. Never
              invent anyone who does not appear in the book or the existing memory. {{heroName}}
              must NEVER appear in "companions".
            - "memories" are short concrete moments told from {{heroName}}'s side — what
              {{heroName}} did, chose, felt or was given ("{{heroName}} gave Rex the golden map").
              Every entry must name {{heroName}} or be plainly about {{heroName}}. Newest first,
              at most 8. Do not write a companion's biography here.
            - "goal" is the one thread running through the whole series, in a single sentence,
              and it is {{heroName}}'s goal. Carry the existing one forward unless this book
              clearly resolved or changed it.
            - "heroTraits" are qualities the STORIES have actually shown about {{heroName}}. Each
              one is one or two WORDS ("brave", "curious", "gentle with animals") — never a
              sentence, and never the evidence for it. At most 4, and only ones the book earned.
            - Add or update the entry in "worlds" for world id "{{worldId ?? "unknown"}}", saying
              how this book left that place.
            - LANGUAGE: every human-readable value — including "description" and "metIn" on each
              companion, and "leftAs" on each world — must be written entirely in
              {{LanguageName(language)}}, because the next book is written in that language. Not
              one English word may appear inside those values, not even a single adjective. Only
              the JSON keys and the world ids stay English. Before returning, re-read every value
              and rewrite any that is not fully in {{LanguageName(language)}}.

            Shape:
            {"companions":[{"name":"","description":"","metIn":""}],"memories":[""],"goal":"","worlds":[{"worldId":"","leftAs":""}],"heroTraits":[""]}

            EXISTING MEMORY:
            {{previousJson}}

            NEW BOOK:
            {{storyJson}}
            """;
    }

    private static string LanguageName(string code) => code.Trim().ToLowerInvariant() switch
    {
        "en" => "English",
        "es" => "Spanish",
        "zh" => "Chinese",
        "ru" => "Russian",
        _ => "Georgian",
    };
}
