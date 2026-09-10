# Character consistency across spreads

The consistency change targets the supporting cast invented by each story: stars, dinosaurs,
animals and other recurring characters. The scenario planner registers them with stable names
and fixed descriptions of their anatomy, colors and markings, including personified objects.
Beki's existing native generation remains the default (`Beki:InsertBekiInGeneration=true`).
The optional exact-PNG compositor is still available when that flag is explicitly false.
No additional model review was enabled. Preview uses a small cover-only planning call based on
just the opening two story pages, alongside the existing child identity call. Its schema has no
supporting cast or spread plans. The saved cover plan is explicitly marked as partial; it cannot
be mistaken for a completed visual scenario.

Full-book fulfillment analyzes the cast and plans all eight spreads once, preserving the preview's
outfit and cover fields exactly. The cover image and child identity are reused. Full analysis is
saved before the first spread is drawn and adopted on subsequent retries. Older previews that
already hold a full scenario remain readable and reusable.

The child keeps the original identity reference plus one fixed rendered appearance anchor.
Each recurring character or object now keeps its first planned appearance as its design source.
Subsequent pages never overwrite that source. A scene receives every relevant source, grouped
by source page, with explicit instructions identifying which characters each image controls.
A character's first appearance must finish before dependent pages start; established characters
can still be drawn in parallel. There is no extra image-generation call to create these sources.

This cache belongs to one book and is reconstructed from stored base images on resume. Missing
source artwork or source evidence causes its dependent pages to redraw together. New prompt
versions prevent partially generated books from combining the old and new generation contracts.
Completed books are not automatically regenerated or changed.

Prompt caching is a latency/cost optimization, not an identity guarantee:
https://developers.openai.com/api/docs/guides/prompt-caching
OpenAI still documents possible recurring-character drift:
https://developers.openai.com/api/docs/guides/image-generation
The generated child, Beki and story creatures remain reference-guided,
so new visual samples still require inspection.

Deployment requires no new environment setting. Beki's generation setting is unchanged.
The GPT Image 2.5 Flare model and the testing-flow switch are unchanged. Full-book generation
can wait longer when a new recurring character is introduced; the preview has no added work.

`LiveCompositeCharacterTests` is an opt-in two-image visual proof using repository test artwork.
Set `ADVENTRYA_CHARACTER_PROOF_KEY` through an authorized secret environment and optionally
`ADVENTRYA_CHARACTER_PROOF_OUTPUT`, then run only that test. It writes two spreads and their
prompts/receipts for visual comparison and makes paid OpenAI requests. Normal test runs skip it.


## Fast preview update — 2026-09-10

AI text proofreading is now off by default (`Providers:StoryPolishEnabled=false`).
Set `Providers__StoryPolishEnabled=true` to opt back into the configured reviewer.
Both composite and legacy v6 stories skip the reviewer call when disabled; structural,
child-name and cast consistency checks remain. The writer stays `gemini-3.1-pro-preview`.

New composite previews are marked `preview-v2` in MasterStoryRuns.PromptVersion (within its existing 10-character limit); existing `preview-v1` revisions remain supported.
They validate locally, make one `gpt-image-2.5-flare` edit request at the configured
fast cover size/quality (default 1200x576, medium), composite the approved Beki PNG,
and render the same vector title/logo composition used by the PDF composer.
The preview screen shows only the front-cover derivative, with its typography already rendered.
The fixed personalized intro is also stored with the revision; the screen has no page-turn controls.

Each of the six themes has five prewritten Georgian title suffixes in `FastPreviewPlan.ThemeTitles`.
The random run UUID selects one deterministically, so retries keep their title and require no AI call.
The literal title is frozen in the revision; name-only edits replace only the child's name, including
for old previews. Paid story generation receives this approved title before writing, and the PDF
keeps it. Random choices can repeat between different books; they are not a round-robin queue.

`CompositeChildArtStyle` is shared by fast covers, full covers and spreads. It explicitly requests
Pixar-style sculpted 3D child features and illustrated skin, while keeping likeness, age and identity
traits. It does not change Beki's approved geometry. Image prompt versions are bumped for provenance.
Reference roles follow [OpenAI's image prompting guidance](https://developers.openai.com/api/docs/guides/image-prompting).
No story, polish, identity extraction, supporting-cast planner, or spread image is
created before payment. The profile screen also no longer calls the AI portrait gate.
The old synchronous guest-preview endpoint returns 410; existing saved previews still work.

Each new preview run owns an immutable revision: base and composited wrap, composition
manifest, generation receipt, frozen title/layout, vector cover PDF, front derivative,
intro derivative and checksums. Checkout sends previewBookId + coverRevisionId and
validates ownership, inputs and source hashes before retaining the run for payment.
Name-only edits create another run/revision that copies the original base; they never
mutate the source order's preview. Photo, age, gender or theme changes require a new cover.

After payment, the existing story writer/polish flow runs once, then full-book visual
planning and identity extraction. The cover base anchors spread one; existing paid-book
supporting-cast caches and native Beki generation remain in use. Fulfilment restores the
saved cover layout and never replaces a new preview's cover. Missing/corrupt evidence
stops for recovery. Admin cover redraw is refused for these purchased previews; spread
redraw remains supported. Admin printing upscale remains separate.

There is no creative or transport retry on the preview image request. A saved base can
resume local composition. An interrupted request with no saved base requires a deliberate
new preview, avoiding an unobserved second image charge. Responses more than 5% away
from the wrap aspect ratio are rejected instead of aggressively cropping the child.
The local title-placement check scores quiet regions; it is not face recognition and
cannot guarantee that the image model respected all semantic safe zones.

The layout remains 512x245 mm; this does not request print-sized generation. Preview
rasterization uses existing pdftoppm at 1200px, already installed by Docker/startup.sh.
No new database columns, environment keys, or packages are required.

Theme title / approved intro background mapping:
- clouds → ღრუბლების ქალაქი → BEKI_Intro_Background_01_Clouds_450x210mm_300ppi_sRGB.png
- space → ვარსკვლავების გზა → BEKI_Intro_Background_02_Space_450x210mm_300ppi_sRGB.png
- forest → მოჯადოებული ტყის მეგობრები → BEKI_Intro_Background_03_Forest_450x210mm_300ppi_sRGB.png
- ocean → მბრწყინავი კუნძულის საიდუმლო → BEKI_Intro_Background_04_Ocean_450x210mm_300ppi_sRGB.png
- magic → სინათლის ქალაქის კარიბჭე → BEKI_Intro_Background_05_Magic_450x210mm_300ppi_sRGB.png
- dinosaurs → დინოზავრების ხეობა → BEKI_Intro_Background_06_Dinosaurs_450x210mm_300ppi_sRGB.png

Verification uses stubbed model calls and real local composition; no paid provider calls
or live six-theme generations were made. FastPreviewTests asserts the call budget,
name-only reuse, checkout revision integrity, paid cover adoption, and frozen typography.
