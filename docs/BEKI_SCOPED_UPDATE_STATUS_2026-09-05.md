# BEKI September 5 scoped source update

Authority: BEKI_FINAL_SCOPE_2026-09-05.md plus the owner's subsequent chat clarifications.

## Confirmed clarifications

- DevOps/operators will configure and test the approved upscaler by hand. That production-runtime test is not a blocker to this source push and has NOT been performed by this session.
- Keep 12 canonical PDF pages. Do not remove the credits spread. Use the SAME existing opening blank-leaf cream under the credits, not a new texture: existing EndpaperPaper is #F3E7D2. Dark-violet credits text is #281B3F. Pattern remains on the right. This supersedes the unavailable-blank-texture requirement in the file.
- Push source to master and mdev; finish on mdev. Do not regenerate paid artwork or rewrite old customer books.

## Source implementation

- Runtime prompts in KidsAdventuresAPI/Services/Story/Composite/CompositeIllustrationPrompt.cs: visual-scenario-v2.4 and cover-child-world-v1.3. COVER-only premium art direction, two/three substantial story-grounded accents, title/head exclusions, actual title/logo geometry. Frozen STORY composite-v1.2 unchanged.
- Actual resolved cover/scenario prompt hashes are logged at generation time by CompositeBookPipeline. No new generation was run, so no executed test-job prompt hash is claimed.
- Approved unchanged HiResColor.svg SHA-256: da8f2fdedfeb203f5dbcc8911f94747713c843ee58f1155b252a219f5ce6a43f.
- Native visible logo bounds: X436–472 mm, Y40–52.7913142 mm on the 512×245 wrap; width36 mm. Exactly20 mm inward from the top/right physical folds. Title X285.5–421.5, Y34–80 mm.
- Final-size helper refuses enlargement or aspect distortion. It only preserves/downsamples a native or externally prepared base to 5315×2480 (story) / 6047×2894 (cover), before exact-Beki re-compositing. Receipts record final dimensions.
- Current canonical preparation accepts RGB without ICC/CMYK/PDF-X-only holds, including startup and asset lock. RGB output is not labelled PDF/X. Legacy explicit CMYK preparation retains its profile checks.
- Preflight now actually enumerates authored PDF pages, and coalesces Georgian per-glyph horizontal positioning before checking duplicate text runs. Duplicate-layer rejection still tested without a layout budget.
- Credits: name hyphen removed, same opening cream background, dark #281B3F type, pattern-right. 12 pages retained.
- Story8 QR targets https://beki.ge without a book-id suffix.
- Known cover head/detail bounds are checked before upscaling and again by the canonical composer. A conflict refuses export. Stored reviews are tied to the exact cover-base SHA. Missing observations are NOT_REVIEWED, not a claimed detector PASS.
- No automatic face detector or paid vision call was added. This geometric safeguard uses recorded human observations; it does not claim automated visual approval of future artwork.

## Cover-layout observations (admin API)

Authenticated Admin routes, order id scoped:

1. GET /api/admin/orders/{id}/cover-layout returns current base SHA, full-wrap dimensions, title rectangle, recorded review and status (NOT_REVIEWED / STALE / PASS / FAIL).
2. GET /api/admin/orders/{id}/cover-layout/base returns the actual child/world base PNG.
3. POST /api/admin/orders/{id}/cover-layout records observations; body:

~~~json
{
  "baseSha256": "<current GET response SHA>",
  "areas": [
    { "kind": "head", "description": "Whole head including hair and face",
      "x": 355, "y": 90, "width": 60, "height": 70 }
  ]
}
~~~

Coordinates above are EXAMPLE bounds, not observations for a real book. Measure in millimetres from the full-wrap top-left (512×245); pixel coordinates convert using x*512/imageWidth and y*245/imageHeight. Include the whole head and any important_detail regions. Reviewer identity/time are server-authenticated. The endpoint records conflicts too, so the next explicit preparation refuses known collisions. It never generates, republishes, charges, or automatically repairs old books. No new admin canvas UI was added.

## Worker upscaler probe

tools/BekiUpscalerProbe is an opt-in .NET8 diagnostic without the application host, database, queue or image provider. Invoke under the worker identity with an approved child/world-only source and a NEW output directory:

~~~text
BekiUpscalerProbe <base.png> <5315|6047> <2480|2894> <absolute-executable-path> "<argument-template using {in} {out} {scale}>" "<approved-tool/model-version>" <new-output-directory>
~~~

It records source/tool-output/final sizes, source/output/executable hashes and operator-supplied tool/model version. Inspect prepared-base.png for artifacts. The executable itself may charge; operator approval is required. Set Beki__PrintPrep__UpscalerPath and Beki__PrintPrep__UpscalerArgsTemplate in the actual Azure worker environment. No executable/model approval, successful production invocation or 300ppi visual acceptance is claimed by this code change.

## Verification and remaining operational acceptance

Offline targeted regression results are stored in Tests/Adventrya.Story.Tests/TestResults/september-scoped.trx. These use local fixtures, not paid book generation. The earlier full-repository attempt hung and was interrupted; do not claim the complete repository suite passed.

Final targeted result: 781 passed, 7 opt-in artifact tests skipped, 0 failed. API build with --warnaserror: 0 warnings/errors. git diff --check passed.

No new compliant real-book PDF or regenerated cover was produced in this session. DevOps upscaler checks and actual new-book visual acceptance (including concrete varnish-candidate identification and QR scan) remain operational checks. Existing customer books were not rewritten. A source push is not proof of Azure deployment completion.
