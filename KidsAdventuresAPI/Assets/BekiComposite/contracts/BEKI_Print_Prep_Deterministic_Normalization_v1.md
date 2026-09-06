# BEKI Print Prep - Deterministic Normalization

**Version:** 1.0
**Date:** 2026-09-06
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
