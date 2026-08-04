# Beki Visual System v1 — Developer Handoff

## 1. Short handoff message — Slack / WhatsApp

გამარჯობა,

გიზიარებ `Beki_Visual_Prompt_System_v1.zip` პაკეტს. ეს უნდა ჩაანაცვლოს ჩვენი მიმდინარე image-generation flow, რადგან ძველ ვერსიაში არ იყო გათვალისწინებული Beki-ს canonical დიზაინი, Visual Bible, ცალკე hero anchor და ავტომატური visual QA.

მთავარი ცვლილებებია:
- ბავშვის ფოტოსგან ჯერ იქმნება structured identity spec;
- შემდეგ იქმნება Visual Bible და ბავშვის canonical hero anchor;
- Beki ინახება backend-ში ოფიციალურ reference asset-ად და ემატება მხოლოდ შესაბამის გვერდებზე;
- cover და თითოეული გვერდი გენერირდება structured scene spec-იდან, არა მოჭრილი story text-იდან;
- თითოეული image გადის Visual Review-ს და საჭიროების შემთხვევაში Repair/Regeneration-ს;
- ქართული ტექსტი, სათაური, CTA და QR image-ში არ გენერირდება — layout-ზე პროგრამულად ემატება.

გთხოვ, პირველ რიგში გაეცნო:
- `docs/backend-integration.md`
- `docs/visual-qa-test-plan.md`

Implementation ჯობია გაკეთდეს feature flag-ის უკან, რათა ახალი და ძველი flow პარალელურად დავტესტოთ, სანამ ახალს საბოლოოდ ჩავანაცვლებთ.

---

## 2. Technical handoff — detailed implementation brief

### Project
**Beki Visual Prompt System v1**

### Objective
Replace the current `BuildStoryImagePrompt()` workflow with a structured, reference-driven visual pipeline that produces consistent personalized storybook illustrations across one cover and twelve interior pages.

The new system must preserve:
- the child’s recognizable identity and apparent age;
- the approved story outfit;
- Beki’s exact canonical design;
- recurring guest-character designs;
- page-to-page continuity;
- text-safe composition for later Georgian layout.

### Source package
Use `Beki_Visual_Prompt_System_v1.zip` as the source of truth.

Important files:
- `prompts/character-identity-analyzer-v1.md`
- `prompts/visual-bible-builder-v1.md`
- `prompts/hero-character-anchor-v1.md`
- `prompts/cover-image-generator-v1.md`
- `prompts/page-image-generator-v1.md`
- `prompts/visual-reviewer-v1.md`
- `prompts/visual-repair-v1.md`
- all JSON schemas under `schemas/`
- `docs/backend-integration.md`
- `docs/visual-qa-test-plan.md`

### Scope
This is not a small edit to the existing flat prompt. Implement a new orchestrated pipeline behind a feature flag, for example:

```text
visual_pipeline_v1 = true | false
```

Keep the current flow temporarily for regression comparison. Do not delete it until the acceptance tests below pass.

### Required pipeline

```text
Approved story JSON
    -> Visual input validation
    -> Child photo quality gate
    -> Character Identity Analyzer
    -> Visual Bible Builder
    -> Hero Character Anchor generation
    -> Hero anchor approval/review
    -> Cover Scene Spec -> Cover Prompt -> Cover generation
    -> Cover Visual Review -> repair/regenerate if needed
    -> Page Scene Spec 1..12
    -> Page Prompt 1..12
    -> Page generation
    -> Visual Review for every page
    -> targeted repair or regeneration
    -> programmatic text/title/CTA/QR layout
    -> final digital/print export
```

### A. Child photo quality gate
Before calling the identity analyzer, validate that the child photo is suitable:
- face is visible and sufficiently large;
- image is not severely blurred;
- face is not heavily occluded;
- the photo is not an unusable group shot;
- lighting is adequate for identity extraction.

If insufficient, return a re-upload request. Do not silently generate a generic child.

### B. Character Identity Analyzer
Input:
- child photo;
- parent-provided age, age band, and eye color.

Output:
- JSON matching the Character Identity Spec in the prompt.

Rules:
- parent-provided age and eye color override uncertain visual inference;
- do not infer personality, ethnicity, hidden hairstyle details, or non-visible traits;
- store uncertainty explicitly.

Persist:
- identity spec JSON;
- analyzer prompt version;
- model version;
- reference image ID/version.

### C. Visual Bible Builder
Input:
- approved story JSON;
- child identity spec;
- child profile;
- approved extra-wish mode;
- official Beki canonical description/reference;
- layout configuration.

Output:
- JSON matching `visual-bible-v1.schema.json`.

The Visual Bible must define:
- hero outfit;
- Beki lock;
- guest-character locks;
- scale relationships;
- world palette/materials/lighting language;
- composition defaults;
- no-text rules.

### D. Hero Character Anchor
Generate a canonical stylized hero anchor before any book page.

References:
1. child real photo — identity only;
2. Visual Bible — approved outfit and render style.

The anchor must:
- show a clear full-body front or 3/4 view;
- preserve apparent age and recognizable facial identity;
- use the approved story outfit;
- use a neutral background;
- contain no other characters and no text.

Do not use page 1 as the hero master reference anymore.

The child photo should still be supplied as an identity reference where supported, while the hero anchor supplies the approved stylized design.

### E. Beki canonical asset
Store the official Beki reference as a backend-controlled immutable canonical asset.

Beki must preserve:
- cream wool body;
- dark purple face and limbs;
- long floppy purple ears;
- warm golden eyes;
- distinctive cream wool tuft;
- round childlike proportions;
- soft tactile texture;
- exact recognizable facial design.

Never allow:
- realistic-sheep redesign;
- color drift;
- short/reshaped ears;
- horns;
- unapproved clothing;
- duplication;
- merging with another character;
- visual dominance over the child.

Attach the Beki reference only when `charactersPresent` includes `Beki`.

### F. Exact cast handling
Every cover/page request must contain an exact cast list.

Example:

```json
{
  "charactersPresent": ["Tini", "Beki", "Snow Sister One"]
}
```

The generated prompt must explicitly say:

```text
Use exactly the listed characters. Do not add, remove, duplicate, or merge any character.
```

Do not auto-add Beki to every page.

### G. Page Scene Specs
Do not send raw story text or truncate it at 600 characters.

Each page must be generated from structured data, including:
- page number;
- exact characters present;
- child action;
- scene summary;
- emotional beat;
- environment;
- key object;
- composition;
- text-safe area;
- continuity state.

Validate against `page-scene-v1.schema.json`.

### H. Prompt assembly
Do not join the final prompt into one flat paragraph.

Use labeled sections in this order:
1. TASK
2. CANVAS AND TEXT-SAFE AREA
3. REFERENCE MAP
4. HERO IDENTITY LOCK
5. HERO OUTFIT LOCK
6. BEKI LOCK, when present
7. SUPPORTING CHARACTER LOCKS
8. EXACT CHARACTERS PRESENT
9. SCENE ACTION
10. COMPOSITION AND CAMERA
11. ENVIRONMENT, LIGHTING, AND MOOD
12. CONTINUITY STATE
13. STYLE
14. NEGATIVE CONSTRAINTS

Do not place these inside the image prompt:
- adventure ID;
- page title;
- raw parent input;
- raw extra-wish text;
- story text intended for readers.

### I. Visual style
Replace direct studio/film imitation language with the product-owned style definition:

```text
Beki Premium 3D Storybook Style:
premium stylized 3D animation, soft tactile materials, rounded child-friendly forms, expressive but identity-preserving faces, warm cinematic rendering, magical emotionally safe environments, controlled richness, and clear focal hierarchy.
```

The child is always the primary focal character. Beki and guest characters are supporting visual elements.

### J. Image generation
Recommended target:
- current GPT Image production model supported by the organization;
- portrait single-page aspect ratio, default `2:3`;
- separate cover and interior configuration;
- draft and final quality modes.

Suggested operating modes:

```text
Draft:
- medium or lower-cost quality
- composition and cast validation

Final:
- high quality for approved hero anchor and cover
- configured final quality for interior pages
```

Use controlled small batches after the hero anchor is approved. Do not launch all twelve pages before the canonical character references are established.

### K. Visual Review
Every generated asset must be reviewed against:
- child photo;
- hero anchor;
- Beki reference, when present;
- Visual Bible;
- scene spec;
- previous continuity state.

Reviewer output must validate against `visual-review-v1.schema.json` and return:
- `approve`;
- `repair`;
- `regenerate`.

Suggested minimum approval requirements:
- hero identity >= 0.80;
- hero age >= 0.90;
- outfit >= 0.90;
- Beki design >= 0.90 when present;
- character count >= 0.95;
- child visual dominance >= 0.85;
- scene action >= 0.85;
- usable text-safe area >= 0.80;
- no text/logo/watermark/fake QR detected.

Thresholds should be configurable after internal testing rather than hard-coded permanently.

### L. Repair vs regeneration
Use targeted repair when:
- Beki’s ears/color/design drifted;
- one prop is wrong;
- one character’s outfit changed;
- a local anatomy issue exists;
- text-safe space needs a local adjustment.

Use full regeneration when:
- the child is not recognizable;
- the cast is substantially wrong;
- the composition misses the scene;
- the wrong world/location is shown;
- multiple continuity rules fail.

Limit automatic retries and log every attempt.

### M. Layout after image approval
The image model must not render:
- Georgian story text;
- title/subtitle;
- page numbers;
- CTA;
- QR code;
- logos or labels.

Add all of these programmatically after visual approval.

### N. Persistence and observability
Store per asset:
- adventure ID as metadata, not prompt content;
- scene/page ID;
- visual prompt version;
- Visual Bible version;
- child reference version;
- hero anchor version;
- Beki asset version;
- image model/version;
- full final prompt;
- generation settings;
- QA scores and decision;
- repair/regeneration history;
- final asset status.

Suggested asset statuses:

```text
pending
identity_ready
visual_bible_ready
anchor_pending
anchor_approved
generating
review_pending
repair_pending
approved
failed
```

### O. Migration plan
1. Add the new pipeline behind a feature flag.
2. Keep old flow available only for comparison.
3. Run fixed regression books:
   - one 2–4 magical book;
   - one 5–7 adventure book;
   - one 8–10 mystery book;
   - one book with Beki on exactly 3–5 pages;
   - one book with recurring guest characters.
4. Compare child identity, Beki consistency, cast accuracy, text-safe area, latency, cost, and retry count.
5. Promote v1 to default only after acceptance criteria pass.
6. Delete or archive dead prompt documentation after migration.

### Acceptance criteria
The implementation is accepted only when:
- one cover + exactly twelve portrait page illustrations are generated;
- the child remains recognizable and age-consistent across all assets;
- the approved story outfit is consistent;
- Beki appears only on specified pages;
- Beki retains the official canonical design;
- the child remains visually dominant when Beki or guests are present;
- no generated image contains reader-facing text, logos, watermarks, or fake QR codes;
- each page has usable text-safe space;
- visual review runs for every asset;
- repair/regeneration decisions are logged;
- final Georgian text and QR are added outside the image model;
- the new flow passes the fixed regression test set.

---

## 3. Developer implementation checklist

### Planning and setup
- [ ] Download and inspect `Beki_Visual_Prompt_System_v1.zip`.
- [ ] Read `docs/backend-integration.md`.
- [ ] Read `docs/visual-qa-test-plan.md`.
- [ ] Create a feature flag for the new visual pipeline.
- [ ] Confirm the exact single-page print/digital ratio; use `2:3` portrait as the current default.
- [ ] Confirm current image-model access and API configuration in the OpenAI project.
- [ ] Confirm privacy, parental-consent, retention, and Zero Data Retention requirements before processing children’s photos in production.

### Remove/deprecate old behavior
- [ ] Remove the 600-character story truncation from the new flow.
- [ ] Stop using page 1 as the only hero consistency anchor.
- [ ] Stop assembling the prompt as one flat paragraph.
- [ ] Remove Pixar/film-name imitation wording from production prompts.
- [ ] Remove page title and adventure ID from image prompt content.
- [ ] Do not pass raw parent free text directly to the image model.
- [ ] Mark `story-image-style.prompt.txt` as deprecated or regenerate it from the new source of truth.

### Canonical assets
- [ ] Upload/store the official Beki reference as a backend-controlled canonical asset.
- [ ] Version the Beki asset.
- [ ] Ensure the asset is attached only when the page cast contains Beki.
- [ ] Store the child photo reference separately from the generated hero anchor.
- [ ] Add versioning for child references and anchors.

### Input validation
- [ ] Validate visual input against `visual-input-v1.schema.json`.
- [ ] Validate each page scene against `page-scene-v1.schema.json`.
- [ ] Reject missing or malformed exact cast lists.
- [ ] Reject visual generation until approved story JSON exists.

### Photo quality and identity
- [ ] Implement the child photo quality gate.
- [ ] Return a clear re-upload state when the photo is insufficient.
- [ ] Implement Character Identity Analyzer.
- [ ] Validate analyzer JSON before storing it.
- [ ] Apply parent age and eye-color overrides.
- [ ] Preserve uncertainty instead of inventing hidden traits.

### Visual Bible
- [ ] Implement Visual Bible Builder.
- [ ] Validate against `visual-bible-v1.schema.json`.
- [ ] Define the hero story outfit once per book.
- [ ] Define Beki’s canonical lock.
- [ ] Define recurring guest locks.
- [ ] Define world palette, materials, lighting, and safety notes.
- [ ] Store Visual Bible version with the adventure.

### Hero anchor
- [ ] Generate the hero anchor before cover/pages.
- [ ] Use the real photo for identity only.
- [ ] Use the Visual Bible for story outfit.
- [ ] Use a neutral background and clean full-body view.
- [ ] Run Visual Review on the anchor.
- [ ] Do not generate book pages until the anchor is approved.

### Cover generation
- [ ] Build cover prompt from `cover-image-generator-v1.md`.
- [ ] Reserve a clean title-safe area.
- [ ] Keep the child visually dominant.
- [ ] Use Beki only if included in the cover cast.
- [ ] Generate no text inside the cover artwork.
- [ ] Run Visual Review and repair/regenerate if needed.

### Interior page generation
- [ ] Build each prompt from `page-image-generator-v1.md`.
- [ ] Use labeled prompt sections.
- [ ] Supply exact characters present.
- [ ] Supply child photo + hero anchor.
- [ ] Supply Beki reference only when Beki is present.
- [ ] Supply approved guest references/locks when needed.
- [ ] Supply continuity state from previous pages.
- [ ] Reserve the specified text-safe area.
- [ ] Generate portrait single-page art.
- [ ] Prevent extra or duplicated characters.

### Beki-specific verification
- [ ] Cream wool is preserved.
- [ ] Dark purple face and limbs are preserved.
- [ ] Long floppy purple ears are preserved.
- [ ] Warm golden eyes are preserved.
- [ ] Cream wool tuft is preserved.
- [ ] No horns are added.
- [ ] Beki is not turned into a realistic sheep.
- [ ] No unapproved clothing is added.
- [ ] Beki remains smaller/secondary to the child.
- [ ] Beki appears only on intended pages.

### Visual Review and repair
- [ ] Run Visual Reviewer for every cover/page/anchor.
- [ ] Validate review JSON.
- [ ] Make score thresholds configurable.
- [ ] Implement `approve`, `repair`, and `regenerate` branches.
- [ ] Use targeted edits for local defects.
- [ ] Use full regeneration for major identity/composition failures.
- [ ] Set maximum retry counts.
- [ ] Log every repair and regeneration.

### No-text and layout
- [ ] Detect and reject generated letters/words/numbers.
- [ ] Detect and reject logos/watermarks.
- [ ] Detect and reject fake QR-like elements.
- [ ] Add Georgian text only after image approval.
- [ ] Add title/subtitle programmatically.
- [ ] Add CTA and real QR programmatically on page 12.
- [ ] Test readability against the reserved text-safe area.

### Continuity
- [ ] Keep child identity and age stable.
- [ ] Keep outfit and accessories stable.
- [ ] Keep recurring guest designs stable.
- [ ] Track object state across pages.
- [ ] Track location/time-of-day progression.
- [ ] Ensure previous visual events are not contradicted.

### Logging and operations
- [ ] Store the full final image prompt.
- [ ] Store model and quality settings.
- [ ] Store prompt/schema versions.
- [ ] Store asset/reference versions.
- [ ] Store QA scores and decisions.
- [ ] Store latency, cost estimate, retries, and failure reason.
- [ ] Add idempotency to prevent duplicate page generation.
- [ ] Use queue-based concurrency and exponential backoff rather than fixed sleeps only.

### Regression and release
- [ ] Run the fixed internal regression set.
- [ ] Compare new vs old flow on quality, consistency, cost, latency, and retries.
- [ ] Confirm all acceptance criteria pass.
- [ ] Make new pipeline default.
- [ ] Archive/remove dead old prompt documentation.
- [ ] Keep rollback capability for the first production release.
