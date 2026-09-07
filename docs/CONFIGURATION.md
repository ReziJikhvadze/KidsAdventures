# Configuration handoff for DevOps — 2026-09-07

Scope: everything the API and the frontend read from configuration, what is **required** on the
production App Service, what changed in the 6–7 September iteration, and the checks to run after
the deploy. Companion to `docs/DEPLOYMENT.md` (workflows, OIDC, approval gate, rollback).

Conventions
- Settings are read from `appsettings.json` (committed defaults) → environment-specific JSON →
  **App Service configuration** (environment variables). The deploy deletes
  `appsettings.Production.json` from the artifact on purpose, so **every production value lives in
  App Service configuration**, written with double underscores: `Beki__PrintPrep__Mode`.
- Bind-time validation runs at startup (`ValidateOnStart`). A wrong value refuses to start; the deploy
  smoke test (`/api/auth/config`) then fails and App Service keeps the previous deployment.
- Migrations (`KidsAdventuresAPI/Data/Scripts/NNN_*.sql`) apply at API startup. This release adds
  `037_OrderGiftWrap.sql` and `038_OrderQuantity.sql` (from master). No schema change came from the
  Print Prep work.

## 1. Required in production (the API does not work without these)

| Key (App Service form) | What | Notes |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | Azure SQL | Hangfire uses the same database. |
| `Jwt__SecretKey` | token signing, ≥64 chars | `Jwt__Issuer`, `Jwt__Audience` default from appsettings; `Jwt__ExpirationMinutes` 120. |
| `OpenAI__ApiKey` | story polish + all BEKI images | Images route: **Images Edit** with `OpenAI__ImageEditModel` (default `gpt-image-2`). |
| `Gemini__ApiKey` | story model | **Required at startup whenever `Providers__Story=Gemini`** (the committed default). CI uses a placeholder; production needs the real key or the app refuses to start. |
| `AzureBlobStorage__ConnectionString` | all book artefacts | `AzureBlobStorage__ContainerName` default `adventurepacks`. `LocalBlobStorage__Enabled` must be **false/absent** in production. |
| `Email__SmtpPassword` | SMTP for magic links, order and admin mail | Also set `Email__BaseUrl` (public web URL) and `Email__ApiBaseUrl` (public API URL): the committed defaults are localhost and would put local ports into customer emails. |
| `Cors__AllowedOrigins__0` | the public web origin(s) | Set `Cors__AllowLocalhostFallback=false`. `Content-Disposition` is exposed (download file names). |
| `Beki__Enabled=true`, `Beki__BookFormatEnabled=true`, `Beki__CompositePipelineEnabled=true` | the BEKI book product | Committed defaults are **false**. Verify these three on the deployed App Service; git deploy success is not proof. |
| `Providers__Images=OpenAI`, `Providers__Story=Gemini` | provider routing | Committed defaults; keep unless changing vendors. `Providers__StoryPolish` optional (`OpenAI`). |
| `Admin__SuperAdminEmails__0` | who may open the admin console | Or set `IsAdmin` on the user row. |
| Payments: `Stripe__*` or `Bog__*` | checkout | `Stripe__SecretKey`, `Stripe__WebhookSecret`, `Stripe__PublishableKey`, price ids, `Stripe__SiteBaseUrl`; or `Bog__Enabled=true` with `Bog__ClientId`, `Bog__SecretKey`, `Bog__SiteBaseUrl`, `Bog__CallbackUrl`. `Stripe__BypassPayment` must be **false/absent**. |
| `Recaptcha__SecretKey` / `Recaptcha__SiteKey` | when `Recaptcha__Enabled=true` | |

Also for production: `Seed__Enabled=false` (absent), `Swagger__Enabled=false`, `ClientIp__TrustedProxyHops=1` (App Service front end), `PasswordlessAuth__ExposeSecretsInResponse=false` (absent), `OpenAI__LogPrompts=false` (committed default).

## 2. Runtime dependencies on the API host

| Dependency | Why | Setting | If missing |
|---|---|---|---|
| Ghostscript `gs` | render validation of every book; legacy CMYK conversion | `Beki__PrintPrep__GhostscriptPath` (default `gs`) | render validation fails → book stays NOT_RELEASABLE, print withheld |
| Poppler `pdftoppm`, `pdffonts` | second renderer + font check (release gate RENDER_VALIDATION / FONT_INTEGRITY) | `Beki__PrintPrep__PopplerPdftoppmPath`, `Beki__PrintPrep__PopplerPdffontsPath` | same as above (a check that did not run is not a pass) |
| Fonts on the host: none needed | Ottia and Noto Sans Georgian ship in `Assets/BekiComposite` and are hash-locked | | |
| Node 22 sidecar for SSR | only when `Frontend__EnableHostedNode=true` (`Frontend__NodePort` 3099, `Frontend__OutputRelativePath` `wwwroot/azure-ssr`) | | frontend served by the front App Service instead |

CI installs `ghostscript poppler-utils libfontconfig1` on Ubuntu. **The API App Service gets them from
the image**: since 2026-09-07 the API ships as a container built from `KidsAdventuresAPI/Dockerfile`,
which installs the same packages and then proves they run (`RUN gs --version && pdftoppm -v &&
pdffonts -v`) — so a base image that ever loses one fails the build rather than the printing.

That replaced a startup script (`KidsAdventuresAPI/startup.sh`, kept as the fallback if the app is
ever put back on code deploy). The script installed the tools on every cold start, which worked and
guaranteed nothing: the container is rebuilt from its image on every restart, deploy, scale-out and
platform patch, so the install ran again each time and an unreachable Debian mirror meant a book that
could not be released, quietly.

Container settings on the App Service: `WEBSITES_PORT=8080`, the image pulled with the app's managed
identity (`acrUseManagedIdentityCreds`), and **no startup command** — the image has an entrypoint.
None of the `Beki__PrintPrep__*Path` settings are needed: apt puts `gs`, `pdftoppm` and `pdffonts` on
PATH, which is where the defaults look.

**Verify with the post-deploy check in §7**: the release-gates document of a finished book lists
`RENDER_VALIDATION = PASS` only when both ran.

## 3. Print preparation (changed 2026-09-06)

| Key | Default | Meaning |
|---|---|---|
| `Beki__PrintPrep__Mode` | `deterministic_lanczos` | **Leave at default.** The stored child/world base is normalized once with Lanczos to the locked press rasters (interiors 5315×2480 px on 450×210 mm, cover 6047×2894 px on 512×245 mm, 300 PPI). No external tool is needed. The other value, `external_super_resolution`, is optional and **only then** requires the two keys below. |
| `Beki__PrintPrep__UpscalerPath`, `Beki__PrintPrep__UpscalerArgsTemplate` | empty | Ignored in the default mode. Required (startup validation) only in `external_super_resolution`. |
| `Beki__PrintPrep__RenderDpi` | 120 | contact sheet / render validation density |
| `Beki__PrintPrep__RequireAllCmyk`, `OutputIntent*` | locked defaults | legacy CMYK path only; the canonical customer/print PDF is RGB. |
| `BekiPrintLayout__PrintAssetJpegQuality` | **95** (was 90) | press rasters: one JPEG encode, 4:4:4, no chroma subsampling |
| `BekiPrintLayout__ScreenAssetJpegQuality` | 90 | reading-copy rasters (non-canonical paths) |
| `BekiPrintLayout__CoverTitleOutlineWidthPt` | 1.5 | vector rim on the cover title (0 disables) |
| `BekiPrintLayout__CoverTitleSizeLadderPt` | `36,32,28,24,20` | **new 2026-09-07.** The cover title is set at the largest size that fits the 136 × 46 mm title box in full; a long title steps down instead of losing a line. The rim scales with the size. Leave at default. |
| `BekiPrintLayout__StoryPanelInkHex` / `StoryPanelOpacity` | unchanged | translucent copy panel under story text |

`PRESS_RESOLUTION` now judges only the output (exact locked pixel sizes, effective PPI from pixels over
placement, no stretch → `PRESS_GEOMETRY`). `PRINT_PREPARATION_HELD` is raised only for a measured defect.

## 4. Image generation request (changed 2026-09-06; defaults unchanged)

| Key | Default | Meaning |
|---|---|---|
| `OpenAI__ImageEditModel` | `gpt-image-2` | the model actually sent (references are always attached) |
| `Beki__SpreadImageSize`, `Beki__CoverWrapImageSize` | `1536x1024` | requested frame; validated at startup against the model family |
| `Beki__AllowExperimentalImageSizes` | false | needed for `3840x2160` (OpenAI marks outputs above 3.69 MP experimental) |
| `Beki__AnchorImageQuality`, `Beki__CoverImageQuality`, `Beki__PageImageQuality` | high / high / medium | |
| `OpenAI__ImageTimeoutMinutes` | 3 | raise when opting into a larger frame |

Optional trial the owner may run on a real book (not a default): `Beki__SpreadImageSize=2048x1152`,
`Beki__CoverWrapImageSize=2048x1152`, `Beki__PageImageQuality=high`, `OpenAI__ImageTimeoutMinutes=6`.
Rollback = remove those four keys. Per-image provenance (model, endpoint, requested size/quality,
returned pixels) is stored beside each base as `spread-NN-generation.json` / `-cover-wrap-generation.json`.

## 5a. Title and Beki placement (changed 2026-09-07)

- The story prompt (`composite-v1.3`) requires the child's name in the title; the name check
  enforces it and, as a last resort, the name is put in front of the title the model wrote
  („ვერიკო და ღრუბლების ქალაქი“). No setting.
- Beki's position on each story spread is chosen deterministically from the generated picture
  (`beki-placement-v1`, contract `BEKI_Beki_Placement_Selection_v1.md`): the exact PNG is placed in
  the calmest spot of her allowed window, never over the fold or the text third, and stays at the
  configured anchor when that spot is already calm. No model call, no setting; the chosen anchor
  is in each spread's composition manifest and `spread-NN-qa.json` (`beki_placement`).

## 5. Preview cover (changed 2026-09-07)

A print-format preview now derives the child identity, plans the visual scenario and draws the real
cover wrap; the six artefacts are stored under `master-runs/{runId}/` and the purchased book adopts
them (no second cover draw; spread 1 is drawn against the cover's child). No new settings. Cost moves
from fulfilment to preview: a preview that is not bought costs about one high-quality image more than
before; a bought book costs the same overall and finishes sooner.

## 6. Other settings (defaults are fine unless noted)

`Beki__GenerationBudgetMinutes` 45, `Beki__PressBudgetMinutes` 15, `Beki__SpreadConcurrency` 4,
`Beki__PageConcurrency` 2, `Beki__ContinuationBaseUrl` `https://beki.ge` (QR destination),
`Beki__PortraitGateEnabled` false, `Gemini__*` models as committed, `OpenAI__MasterStoryModel`
`gpt-5.6-sol`, `WifisherSms__*` (SMS OTP, off by default), `GoogleAuth__*`, `GoogleMaps__*`,
`PasswordlessAuth__*` (OTP/magic-link limits), `PrintLayout__*` (legacy print), `Email__From*`.

## 7. Post-deploy checks (both sides must work)

1. `GET /api/auth/config` → 200 (the deploy smoke test).
2. Log in as an admin; open Admin → orders → a completed BEKI book.
3. `POST /api/admin/books/{id}/reprepare-print` (button "ბეჭდვის ხელახლა მომზადება") → 200; `press-status.json`
   shows `print_prep_mode: deterministic_lanczos`, `failed_gates: []`, `normalizer: imagesharp-3.1.12-lanczos3`.
4. Release gates: every gate PASS except `VISUAL_QA` = `REVIEW_SKIPPED_BY_POLICY` with
   `print_awaiting_human_approval: true`; `RENDER_VALIDATION` PASS proves Ghostscript **and** Poppler are installed.
5. Approve the contact sheet → verdict `RELEASABLE`, `pressFilesPublished: true`; admin print download 200;
   the customer download returns the same bytes, file named after the book title.
6. Alarms for the pack: `PRINT_PREPARATION_HELD` reviewed by `system:print-reprepare`.

## 8. Admin endpoints added in this iteration

| Route | Purpose |
|---|---|
| `POST /api/admin/books/{id}/reprepare-print` | re-prepare print for a Completed book from stored artwork; no generation, no charge; snapshots the live files to `previous/{ts}/` and rolls back on refusal |
| `POST /api/admin/books/{id}/recover-customer-pdf` | now also finishes into print when gates pass |
| `POST /api/admin/orders/{id}/approve-review` | contact-sheet-bound human approval; closes VISUAL_QA for review-skipped books and publishes the print file |

Re-preparation, recovery, regeneration and the reconciliation sweep share the Hangfire distributed lock
`beki-pack:{packId}`; the API and the Hangfire workers run in the same process.
