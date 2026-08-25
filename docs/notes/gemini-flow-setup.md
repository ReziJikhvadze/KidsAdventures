# The Gemini flow — what was set, and how to run it again

Branch: `flow-misho`. Recorded 2026-08-25, against commit `c24e234`
("Let the editor be a different model from the writer").

The vendor split this describes is the one that commit added. Nothing here is
code: every value below is configuration, and none of it is committed. The repo
still says OpenAI for everything in `appsettings.json`, which is deliberate —
an installation that sets nothing keeps the pipeline it had.

**No secret appears in this file.** Two API keys are needed and both are named
without their values. They live in `dotnet user-secrets`, outside the tree, the
same rule `local-setup.md` sets for every other secret here.

## The flow

| Stage | Vendor | Model | Where the model name comes from |
|-------|--------|-------|--------------------------------|
| Story generation | Gemini | `gemini-3.6-flash` | `Gemini:StoryModel` |
| Text polish — grammar, spelling, age-safety | OpenAI | `gpt-5.6-sol`, reasoning `high` | `OpenAI:MasterStoryModel` |
| Cover + 8 spreads | Gemini | `gemini-3.1-flash-image` (Nano Banana 2) | `Gemini:ImageModel` |
| Illustration QA review | — | none | switched off in code, see below |
| Child-photo reading | Gemini | `gemini-3.6-flash` | `Gemini:VisionModel` |

The photo reading follows `Providers:Images`, so it is Gemini here. It is worth
saying explicitly because the call site asks `IOpenAiService` for it and the
type name suggests otherwise — `AiServiceRouter` is what actually answers.

## Setting it up

Both keys first. The values are not in this file on purpose:

```bash
dotnet user-secrets set "Gemini:ApiKey" "<your Gemini key>" --project KidsAdventuresAPI/KidsAdventuresAPI.csproj
dotnet user-secrets set "OpenAI:ApiKey" "<your OpenAI key>" --project KidsAdventuresAPI/KidsAdventuresAPI.csproj
```

An OpenAI key is required even though Gemini writes and draws: the polish pass
is OpenAI in this configuration, and the legacy A5 path never routes at all.

Then the six settings that make the flow:

```bash
P="--project KidsAdventuresAPI/KidsAdventuresAPI.csproj"
dotnet user-secrets set "Providers:Story"                  "Gemini"           $P
dotnet user-secrets set "Providers:Images"                 "Gemini"           $P
dotnet user-secrets set "Providers:StoryPolish"            "OpenAI"           $P
dotnet user-secrets set "Gemini:StoryModel"                "gemini-3.6-flash" $P
dotnet user-secrets set "OpenAI:MasterStoryModel"          "gpt-5.6-sol"      $P
dotnet user-secrets set "OpenAI:MasterStoryReasoningEffort" "high"            $P
```

In zsh that `$P` does not word-split — write the flag out in full, or run it
under bash.

And the four the Beki book needs regardless of vendor:

```bash
dotnet user-secrets set "Beki:Enabled" "true" --project KidsAdventuresAPI/KidsAdventuresAPI.csproj
dotnet user-secrets set "Beki:BookFormatEnabled" "true" --project KidsAdventuresAPI/KidsAdventuresAPI.csproj
dotnet user-secrets set "LocalBlobStorage:Enabled" "true" --project KidsAdventuresAPI/KidsAdventuresAPI.csproj
dotnet user-secrets set "Stripe:BypassPayment" "true" --project KidsAdventuresAPI/KidsAdventuresAPI.csproj
```

`Beki:Enabled` is `false` in `appsettings.json` and the book does not exist
without it. `Stripe:BypassPayment` takes the free-order route through the real
fulfilment path, which is what makes an end-to-end run possible without paying —
it must be off anywhere real money is involved.

## Values that were never set, and still decided the run

Left at their code defaults. They are listed because a run that differs from the
numbers below probably differs here first.

| Setting | Default | What it did |
|---------|---------|-------------|
| `Gemini:ImageModel` | `gemini-3.1-flash-image` | Nano Banana 2. From `appsettings.json`, not overridden |
| `Gemini:ImageSize` | `2K` | 2528×1696 spreads, 2048×1374 cover |
| `Gemini:VisionModel` | `gemini-3.6-flash` | reads the child's photograph |
| `Beki:SpreadConcurrency` | `2` | two spreads drawn at once — 8 spreads took four waves |
| `Beki:SpreadRegenerationAttempts` | `0` | no redraws. Moot while QA review is off |
| `Gemini:TimeoutMinutes` | `12` | per call |
| `Gemini:RetryAttempts` / `RetryBackoffSeconds` | `3` / `5` | 429 and 5xx retried at 5s, 10s |

`QaReviewEnabled` in `BekiBookGenerator.cs` is `false` — a constant, not a
setting. Illustrations are single-shot on this branch: no reviewer, no redraw.
`SpreadRegenerationAttempts` and `Gemini:VisionModel`'s QA half are dormant
until that flips.

## Restarting is part of the procedure

User secrets are read once at startup and `IOptions<T>` snapshots there. A
changed setting does nothing until the API restarts, and a run started before
the restart used the old value. This is easy to get wrong and hard to see: the
first measured run here silently used `gemini-3.7-flash` because the process
predated the write of `3.6`.

Confirm by comparing the two:

```bash
stat -f "secrets: %Sm" -t "%H:%M:%S" ~/.microsoft/usersecrets/e7b3905a-1017-4947-afaa-6235366c1e29/secrets.json
ps -eo pid,lstart,command | grep "[d]otnet run --project KidsAdventuresAPI"
```

The process must be the later of the two.

## What it produced

One book, `ომიკო და ვარსკვლავების რუკა`, airplanes, age 3, Georgian, 8 spreads.

| Phase | Duration |
|-------|----------|
| Story — Gemini writes, sol edits | 184s |
| Cover | 27s |
| 8 spreads, two at a time | 130s |
| PDF and telemetry | 19s |
| **Total** | **6m 00s** |

10,226 prompt / 11,161 completion tokens. That completion count is two whole
books: the polish pass returns the entire book rather than a patch list, which
is what makes the merge safe and also what doubles the text bill.

Text was 184s of 360 — more than half, and the larger half is sol at reasoning
`high`. Images were 157s for nine pictures. If this needs to be faster, the
polish effort is the lever, not the illustrator.

Spreads land about 5 MB each as PNG: roughly 40 MB of artwork per book at 2K.

## Two things that will bite

**Nano Banana 2 has no free tier.** A key without billing returns
`429 RESOURCE_EXHAUSTED` with `limit: 0` — not a used-up allowance, no allowance
at all. The client treats every 429 as transient and retried it three times
before failing the whole book, which cost about two minutes and produced an
error reading only `Gemini returned 429.` A `limit: 0` quota is a configuration
error and should fail immediately; it does not.

Check a key before blaming the code:

```bash
curl -s -X POST "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-flash-image:generateContent?key=$GEMINI_KEY" \
  -H 'Content-Type: application/json' \
  -d '{"contents":[{"parts":[{"text":"a single small red dot on white"}]}]}' -o /dev/null -w '%{http_code}\n'
```

**The stored model name does not say which vendor ran.** `MasterStoryRuns.Model`
recorded `gpt-5.6-sol` for a book Gemini wrote. `MasterStoryService.ModelName`
always reads `OpenAiOptions.MasterStoryModel` whatever the provider, and
`GeminiStoryModelClient` deliberately ignores the model it is passed in favour
of `Gemini:StoryModel`. Both halves therefore report the same OpenAI name, the
"writer + editor" attribution added in `c24e234` never triggers, and two runs on
different vendors are indistinguishable in the database.

Until that is fixed, tell the vendors apart by the artwork instead. OpenAI's
path is fixed at 1536×1024; Gemini at `ImageSize: 2K` produced 2528×1696.

```bash
sips -g pixelWidth -g pixelHeight KidsAdventuresAPI/.localblob/adventurepacks/<pack>/<book>/spread-01.png
```

## Running it

`local-setup.md` covers the machine. In short: `./scripts/local-db.sh up` for
the database — Docker or colima must be running first — then the API and the
frontend, which are registered in `.claude/launch.json` as `adventrya-api`
(port 5080) and `adventrya-web` (port 8080).

Incoming requests are not logged: `Microsoft.AspNetCore` sits at `Warning`. What
does log is drowned anyway — `LocalFileBlobStorageService` writes "Blob storage
is a local folder" once per scoped resolution, hundreds of lines an hour, and it
evicted every useful line from the buffer while this run was being diagnosed.
The database is more reliable than the log for finding out what happened:

```sql
SELECT Status, ProgressPercent, ErrorMessage FROM AdventurePacks ORDER BY CreatedAt DESC;
SELECT Status, Model, PromptTokens, CompletionTokens FROM MasterStoryRuns ORDER BY CreatedAt DESC;
```

Note also that a fulfilled order can own a failed book. `RetryStalledFulfilmentAsync`
sweeps every five minutes but only looks at orders still `Paid`, so a pack that
failed under an order already marked `Fulfilled` is never retried and never
appears on the dashboard.
