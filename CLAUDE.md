# Beki / KidsAdventures - working guide

Personalised children's books in Georgian. A parent previews a book for their child, pays, and the
pipeline draws it; some are also printed and posted. Live at beki.ge.

Read this before touching the book pipeline. It exists because the same facts kept being
re-derived from scratch, and the ones in **Where state lives** are the ones that cost the most.

## Stack and layout

| | |
|---|---|
| API | ASP.NET Core, net8.0, `KidsAdventuresAPI/` - ~86k lines C#, 170 service files |
| Frontend | React + TanStack Router + Vite, `KidsAdventuresAPI/wwwroot/` - 243 ts/tsx files |
| Tests | one project, `Tests/Adventrya.Story.Tests/` - 120 files, ~1900 tests |
| Data | SQL Server via Dapper, no EF. Numbered scripts in `KidsAdventuresAPI/Data/Scripts/` |
| Jobs | Hangfire on the same SQL database |
| Files | Azure Blob Storage, or a local folder when `LocalBlobStorage:Enabled=true` |
| Copy | Georgian throughout, including error messages shown to parents |

The weight is concentrated. Five files carry most of the book pipeline:

- `Services/Story/BekiPackFulfillment.cs` - 5,515 lines. The fulfilment job, recovery, re-prepare,
  and `BekiPackBlobs` (every blob name) all live here.
- `Services/Story/Composite/CompositeBookPipeline.cs` - 4,991 lines. Planning and drawing.
- `Services/Story/BekiPdfComposer.cs` - 3,841 lines. Laying out the PDF.
- `Services/Story/BekiReleaseGates.cs` - 1,973 lines. The verdict.
- `Services/Implementations/AdventureGenerationService.cs` - 1,749 lines. The legacy pipeline.

Comments in this codebase are long and narrative: they explain the fault a line was written to fix.
They are worth reading, but when hunting a condition, grep for the condition rather than reading
top to bottom.

## The money path

```
preview (free, guest ok)  ->  order  ->  payment  ->  fulfilment  ->  release  ->  download
                                                                              \->  print order -> parcel
```

1. **Preview** - `MasterStoryService` / `FastPreviewService`. A story plan plus one cover image.
   Guest previews live in `GuestPreviews` / `MasterStoryRuns`.
2. **Order** - `OrdersController` -> `OrderService`. Bog (Georgian cards) when `Bog:Enabled`,
   otherwise Stripe. Both confirm two ways: a webhook, and `POST /api/orders/{id}/confirm` which
   asks the provider directly when the parent returns. The pull path is what makes local testing
   possible without a public callback url.
3. **Fulfilment** - `BookFulfillmentService.FulfillAsync` creates the pack and enqueues
   `IBekiPackFulfillment.ProcessAsync` on Hangfire.
4. **Drawing** - `CompositeBookPipeline`: story (Gemini), identity spec and images (OpenAI),
   8 spreads plus a wrap cover, composited and laid out.
5. **Release** - the job evaluates the gates and records the verdict, then marks the pack
   `Completed` and records the release flags.
6. **Download** - `GET /api/adventure-packs/{id}/download` composes the PDF for that request.
7. **Print** - `PrintOrdersController` / `PrintOrderService`, parcel rows in `PrintOrders`.

## Two pipelines

`AdventurePacks.GenerationPipeline` is `'beki'` or `'legacy'`. Nearly everything new is Beki; legacy
is the pre-2026 per-page flow and still owns rows in production.

`AdventurePackStatus`: `Pending, Generating, Completed, Failed, GeneratingStory, StoryReady,
GeneratingPdf`. Note the asymmetry: on legacy, `StoryReady` is a finished book waiting to be
illustrated on demand; on Beki it is a stage inside a running job.

## Where state lives

**This is the section to read.** Three stores, and knowing which answers what is the difference
between a ten-minute fix and a four-hour one.

| Question | Answer lives in | Not in |
|---|---|---|
| What status is this book | `AdventurePacks.Status` (SQL) | - |
| May the family download it | `AdventurePacks.CustomerPdfReleased` (SQL bit) | any url column |
| May the printer have it | `AdventurePacks.PressFilesReleased` (SQL bit) | any url column |
| Why is a download held | `release-gates.json` in **blob storage**, read by `IBekiDownloadStatusService` | SQL |
| Which gates failed, and how badly | `release-gates.json` in **blob storage** | SQL |
| Where are the pictures | blob storage, named by `BekiPackBlobs` | SQL |
| Where is the PDF | **nowhere.** Composed per request | anywhere |
| What is running | Hangfire tables in the same database | - |

**No PDF of a book is stored.** Not the reading copy, not the printer's. Both are built for the
click that asks: `ComposeCustomerPdfAsync` for the parent, `PreparePrintPdfAsync` in the operator
panel. `PdfUrl` and `PrintPdfUrl` are therefore **null on every book made since 2026-09-12** and
survive only for older rows.

That single change broke nine separate places in one day, all of them reading a null url as "the
file does not exist" and therefore "the book is not ready". If you change what a column means,
`grep -rn "ColumnName"` across the whole solution and read **every** hit before writing code.

**The verdict is not a permission slip for the family.** `BekiReleaseGateReport` answers whether a
book is fit to *manufacture*: colour, geometry, resolution, press gates. It governs the print queue,
the console and the alarms. It must not decide whether a parent may open the book they bought - a
finished book downloads. That rule was learned the expensive way on 2026-09-13.

Gate classes: `digital`, `press`, `shared`, `package`. `CustomerPdfMayPublish` reads the digital
class; `PrintReady` reads press.

## Blob layout

All under `{userId}/{packId}/`, named by `BekiPackBlobs` (inside `BekiPackFulfillment.cs`, ~line
100-350): `spread-N.png`, `cover.png`, `wrap-*.png`, `fulfilment.json` (the manifest),
`release-gates.json` (the verdict), `human-approval.json`, QA and receipt files per stage.
Press artifacts use the flat `{userId}/{packId}-interior.pdf` shape and mostly no longer exist.

## Running locally

`.claude/launch.json` **inside `KidsAdventuresAPI/`** (not the repo root one) has three configs:

- `adventrya-api-local` - **use this.** Development, LocalDB `KidsAdventuresLocal`, local blob
  folder `.localblob/`. Never touches Azure.
- `adventrya-api` - Production profile against the **live Azure database**. Do not run it.
- `adventrya-web` - Vite on 8080. That port matters: emailed links are built on it.

Once per machine: `sqllocaldb start MSSQLLocalDB` (it is Stopped after every reboot), and the
database must exist. Migrations apply themselves at startup.

Secrets go in `dotnet user-secrets` (UserSecretsId `e7b3905a-...`), never in the tree.
`appsettings.Production.json` is gitignored and holds the **live Azure connection string** - loading
it means touching production.

In Development, `PasswordlessAuth:ExposeSecretsInResponse` is true, so sign-in codes come back in
the API response and no mail is needed.

**Ghostscript is required.** `BekiPrintPrep` and `BekiDigitalPrep.Linearize` both shell out to `gs`.
Without it on PATH, press preparation *and the parent's own reading copy* refuse. It is not
installed on this machine, which is why a local end-to-end download will fail until it is.

## Testing

```bash
DOTNET_ROLL_FORWARD=Major dotnet test KidsAdventures.sln --no-build
```

Build the **solution**, not the API alone: an API-only build hides broken test doubles.

**The 61 failures are mostly not real.** At least 32 trace directly to the missing Ghostscript
("Print preparation refused: Ghostscript is not available as 'gs'"), and several of the
`Assert.Equal` failures are downstream of the same absence. Installing Ghostscript is the highest
-value thing anyone can do for iteration speed here: it would turn a 3.5-minute run whose result has
to be diffed against a recorded baseline into a glance at green or red.

Until then, a regression check costs two full runs and a diff:

```bash
dotnet test ... 2>&1 | grep -oE "^\[xUnit[^]]*\]\s+\S+ \[FAIL\]" | sed -E 's/^\[[^]]*\]\s+//; s/ \[FAIL\]$//' | sort > fails.txt
comm -13 baseline.txt fails.txt   # anything printed is a regression
```

Test doubles implement wide interfaces by hand. When adding an interface member, give it a default
implementation or every double stops compiling - the codebase does this deliberately and says so in
the xml docs.

## Deploy

`.github/workflows/deploy.yml` on every push to master. **Migrations apply at API startup against
the live Azure SQL, so a deploy is a schema change.** CI (`ci.yml`) runs a secret scan and the build
on every push and PR. A `pre-push` hook typechecks the frontend locally.

## Traps

- A running `dotnet run` holds `bin/Debug/net8.0/KidsAdventuresAPI.exe`, so a build fails with
  MSB3027 until it is stopped. The DLL can be replaced while running; the exe cannot.
- Two `.claude/launch.json` files exist. The API-scoped one is the one with the local profile.
- Story generation costs real money (Gemini) and images cost more (OpenAI `gpt-image-2.5-flare`).
  Never start a create-book run without explicit permission in that conversation.
- The withheld sweep (`ReconcileWithheldAsync`) is **not** scheduled. It runs only when the release
  policy changes. Recurring jobs are just: auth purge, guest-run purge, order retry, stale-generation
  sweep.
- House style: plain hyphens, no em dashes, in copy and story text.
- Other people work in this tree at the same time. Stage your own hunks.

## How to debug a bug here, in order

1. **Ask what the behaviour should be**, not only what changed. The four-hour day happened because
   the question answered was "what did yesterday's commit break" instead of "what does the product
   need to do". Restoring a broken mechanism faithfully is not the same as making it work.
2. Get the symptom precisely: which screen, which button, which user, and the production log if
   there is one. A stack trace with a message names the file and line outright.
3. Find the condition that produces it, then **grep every other reader of the same state** before
   editing. One `grep -rn` over the column or flag beats four rounds of "another one".
4. Check which store answers the question (the table above). A question SQL cannot answer will be
   answered wrongly rather than not at all.
5. Change it, build the solution, diff the failures against the baseline, then push.
