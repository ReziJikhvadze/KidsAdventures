# BEKI Print Prep - Deterministic Normalization

**Version:** 1.1
**Date:** 2026-09-06, amended 2026-09-08 (§9)
**Status:** Authoritative amendment to `BEKI_Acceptance_Gates_v1.json` for the current book format

## 1. Authority

Product Owner override of 2026-09-06.

The physical proof `BEKI_Nina_Golden_Print_Proof_v1` was printed, reviewed and its sharpness
accepted. The earlier handoff made that physical proof the decision gate for whether external
super-resolution was required; the proof has now been inspected, so the decision is resolved:

> **No external AI upscaler is required for this phase.**

This amendment records that decision and states what the resolution gate is allowed to judge in its
place. It changes no locked millimetre geometry, no colour rule, no font rule, and no text rule.

## 2. The two modes

`Beki:PrintPrep:Mode`, parsed case-insensitively and ignoring `_` and `-`.

| Mode | What it requires | What it does |
|---|---|---|
| `deterministic_lanczos` (default) | Nothing. No binary, no path, no network, no cost. | Aspect-safe minimal centred crop, then exactly one Lanczos3 resize to the locked pixel size, then 300 PPI metadata and a lossless PNG intermediate. |
| `external_super_resolution` (opt-in) | Both `Beki:PrintPrep:UpscalerPath` and `Beki:PrintPrep:UpscalerArgsTemplate`, or startup fails by name. | Runs the configured executable, then reduces its whole-factor output to the locked size. |

`UpscalerPath` and `UpscalerArgsTemplate` are **optional and ignored** in the default mode. Nothing
fails for their absence: not startup, not print preparation, and not `PRESS_RESOLUTION`.

The aspect-safe crop is bounded. A reconciliation that would discard more than **0.5 % of either
axis** is refused rather than performed, because at that depth it is no longer rounding — the
artwork has a composition the sheet does not, and it must be re-cropped upstream. Stretching it into
the sheet instead is never permitted.

## 3. Pipeline order

1. OpenAI child/world base render.
2. Aspect-safe minimal crop (only when the ratios disagree, and only within the 0.5 % allowance).
3. **One** deterministic Lanczos3 normalization to the exact locked raster.
4. Exact-Beki re-composition from the hash-verified pose.
5. Vector text, title, logo and QR layers.
6. **One** JPEG encode, quality 95, 4:4:4 (composer).
7. Canonical RGB PDF, 12 pages.
8. `PRESS_RESOLUTION` measures the output of step 7.

Rasters already at the exact locked size are not resampled again; they pass through by reference.

## 4. PASS criteria

A press package passes print preparation when all of the following hold on the produced PDF:

- Cover: `512 x 245 mm` sheet carrying a `6047 x 2894 px` raster.
- Every interior full-spread: `450 x 210 mm` sheet carrying a `5315 x 2480 px` raster.
- Effective PPI at placement rounds to 300, within the existing PDF point-rounding tolerance
  (`required_press_raster_ppi - 0.5`).
- Exact pixel dimensions. There is no tolerance on the pixel count: the normalization delivers those
  numbers or the book does not have them.
- Aspect preserved: the placed-millimetre ratio and the pixel ratio agree within 0.5 %.
- No stretch anywhere. `ResizeMode.Stretch` is reachable only after the ratio has been reconciled by
  the crop, so the two axes always scale by the same factor.
- The required vector layers are present: story text, cover title, logo and QR.
- The visual normalization review of the rendered contact sheet reports no blocking artifact.

## 5. FAIL criteria

- Wrong pixel dimensions on any press raster.
- Effective PPI below the tolerance.
- Metadata-only DPI substitution: a raster re-tagged 300 DPI without the pixels to support it. The
  gate computes PPI from pixels over placed millimetres and never reads a DPI tag, so this fails.
- Stretched artwork, or artwork cropped past the 0.5 % rounding allowance.
- A missing full-sheet raster on a canonical page.
- A failed visual normalization review.

> **Do not fail based on the name or provenance of the resizing tool.**

The receipt still records the tool, the linear factor, the crop rectangle and the mode, because a
physical proof is inspected against what somebody actually did. But a receipt naming `lanczos3`, or
naming nothing at all, is not by itself a defect: the measured output is the defect or it is not.

## 6. Encoding

- JPEG quality 95, 4:4:4, chroma subsampling disabled.
- **Exactly one** encode per raster. The composer performs it; every stage in front of it hands on a
  lossless PNG intermediate.
- Lossless output is permitted only within the canonical-PDF size budget. Local measurement on the
  reference book: **74 MiB lossless against 35.75 MiB JPEG at the same 300 PPI**. Lossless is
  therefore not adopted.

## 7. Note on generation size

If a larger generation frame is opted into (`Beki:SpreadImageSize`, `Beki:CoverWrapImageSize`), raise
`OpenAI:ImageTimeoutMinutes` with it. A 4K render takes materially longer to return than the proven
`1536x1024`, and a timeout in the middle of a paid generation costs the book, not the frame.

## 8. Supersedes

This amendment supersedes:

1. The sentence **"interpolation-only upscaling is a failure"** in
   `BEKI_Acceptance_Gates_v1.json`, `hard_gates[PRESS_RESOLUTION].requirement`. The remainder of that
   requirement — "Every press raster has at least 300 effective source PPI at placement size" —
   stands unchanged, and is now measured against the exact locked pixel sizes as well.
2. The super-resolution remedy in `BEKI_Deliverables_Audit_and_Claude_Correction_Brief_v1.md`,
   finding **P1-01**: "use approved super-resolution followed by a 100% physical proof inspection".
   The physical proof inspection happened and passed; the super-resolution half is retired for this
   phase. P1-01's other half — "Preflight must report source effective PPI, not only the embedded
   raster dimensions" — stands and is unchanged.

The supplier documents are **not edited**. They remain the record of what was delivered; this file
is the record of what the owner decided afterwards, and the preflight report names it under
`resolution.decision_record` so an artifact always says which rule it was measured under.

## 9. Amendment 1.1 (2026-09-08) — what the press stage keeps, and what it costs

### 9.1 The defect

Owner report, 2026-09-08: *"After the progress bar reaches 91 % ('preparing the canonical book') the
job takes more than an hour on the server."*

Nothing stored by the stage could say which part of the hour it was. The stage was profiled on the
stored book `54cba4b3` (nine rasters: eight interior spreads and the cover wrap). Wall time on a
ten-core development machine, which is not the deployment — the **proportions** are the finding:

| Step | Before | After |
|---|---|---|
| read nine hash-verified bases | 0.15 s | 0.15 s |
| normalize nine rasters (three at a time) | 14.3 s | 2.8 s |
| `FinalSize` on nine prepared rasters | 1.6 s | 0.02 s |
| press composite ×9 (decode, paste the approved pose, PNG encode) | 40.2 s | 14.0 s |
| store the press evidence | **208 MB** in 18 PNGs | **60 kB** in 9 receipts |
| compose the canonical PDF (one JPEG q95 4:4:4 per raster) | 11.1 s | 10.7 s |
| preflight | 0.15 s | 0.13 s |
| render validation (Ghostscript, Poppler, QR, contact sheet) | 18.0 s | 9.6 s |
| **total** | **85.6 s** | **37.6 s** |

The 208 MB is the finding that matters for the App Service. Locally it is a fifth of a second of
disk; over a network to a storage account it is minutes, on every press run, for files no reader,
download, printer or gate has ever fetched back.

### 9.2 The press evidence: receipt, not raster

The intermediate `print/{role}-base.png` and `print/{role}-composite.png` are **no longer stored**.

`print/{role}-composition.json` stays and grows. It is now
`beki-press-composite-receipt-v1`: the partner composition manifest verbatim — the same closed
`composition_manifest_v1` document, still diffable against the reference implementation's — inside
an envelope that adds, for both the base and the composite, the **SHA-256 and the byte length** of
the image, and states plainly that the named files were not retained.

The evidence for a press raster is therefore:

1. the composition receipt above, which identifies both images exactly and is verifiable within the
   run by re-preparing the same stored base; and
2. the **one** JPEG q95 4:4:4 encode of the composite embedded in the canonical PDF, which is the
   artifact the printer actually receives.

`EXACT_BEKI` is unchanged: the stored base and the approved pose are still hash-verified before
anything is composited, the re-composition still refuses a different pose asset, and both hashes are
still written down. What is no longer kept is a second copy of an image the receipt already
identifies.

### 9.3 Cost changes that do not touch a gate

- **PNG intermediate compression.** The lossless PNG intermediate of §3 step 3 and §6 is written at
  deflate `BestSpeed` rather than the default level — 3859 ms against 535 ms per interior raster.
  PNG is lossless at every level, so the pixels handed to the composer are identical and the single
  JPEG encode of §6 is untouched; only the size of a temporary buffer changes.
- **`FinalSize` reads the header.** Three of its four answers are decided by two integers, so it
  reads the PNG header (6 ms) instead of decoding thirteen megapixels (171 ms) to be told a size the
  header already states. It decodes only when it actually has to resize, which on the default mode
  is never.
- **The nine composites are made in parallel**, bounded, in the same way the normalizations already
  were. The order of what comes out is unchanged: results are collected by index, and the receipts,
  the artwork and the recorded problems are assembled from that afterwards.
- **The two renderers run at the same time.** `RENDER_VALIDATION` still requires both Ghostscript
  and Poppler, still reads the `pdffonts` table, and still fails on a `skipped` run. They are simply
  started together instead of one after the other — 8.5 s + 8.3 s sequential against 8.5 s
  concurrent — and the evidence is asserted identical either way. On a single-core deployment they
  stay sequential, because two renderers on one vCPU cost the same time and twice the memory.
- **Blob copies are the storage service's.** `IBlobStorageService.CopyAsync` copies a blob inside
  the store: server-side on Azure, a file copy locally, and download-then-upload for any
  implementation that has neither. The snapshot a print re-preparation takes before it replaces a
  finished book — and the rollback that puts it back — no longer move a fifty-megabyte PDF through
  the application twice.

### 9.4 New settings

| Key | Default | What it is |
|---|---|---|
| `Beki:PrintPrep:Parallelism` | `min(cores, 3)` | How many press rasters, composites and render validations run at once. Was the constant 3, which on a one-vCPU plan bought no speed and three times the resident memory. An explicit value is honoured up to 16. |
| `Beki:PrintPrep:MaxImagePoolMegabytes` | `256` | The ceiling on ImageSharp's pooled pixel buffers, set once at startup. Zero leaves ImageSharp's machine-sized default in place. |

### 9.5 What a run now says about itself

- `press-status.json` gains `timings_ms` — `read_bases`, `normalize`,
  `normalize_slowest_raster`, `normalize_fastest_raster`, `composite`, `upload_receipts`,
  `compose_pdf`, `preflight`, `snapshot`, `publish`, `total` — and `parallelism`.
- The render report (`render-canonical-book.json`) gains `timings_ms` — `ghostscript`, `pdftoppm`,
  `pdffonts`, `renderers`, `read_pages`, `qr_scan`, `contact_sheet`, `total` — and
  `concurrent_renderers`.
- One `Information` log line per step, naming the pack.

Between them the whole stage is accounted for, in documents an operator already opens when printing
is held.
