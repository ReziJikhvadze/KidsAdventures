# BEKI Child/World Image Prompt Template v1.5

**Prompt version:** `child-world-image-v1.5`  
**Status:** Implementation source  
**Purpose:** Generate a text-free child-and-world base image. Beki is composited later from an approved transparent PNG.

## v1.5 changelog

Amended against the supplier's production rejection of 2026-08-31 (P0-A "full-half-page wash" and P0-B "visible centre seam" — measured as one defect: every shipped Story base carried a model-painted milky veil over the complete text-side half, terminating in a razor edge at the fold, luma delta 43–75 between the halves).

- **Observed defect: the veil was prompt-invited three ways and banned only in its dark form.** The template (1) called the canvas a "two-page spread"; (2) asked for the text third "gently lightening toward the outer edge" — a model that is told to lighten a third lightens the half it was told has its own identity; (3) used the "central low-information zone" as a spatial landmark twice in the composition resolver, giving the veil a named edge to stop at; and (4) the unbroken-painting negative listed only dark artefacts (shadow band, dark strip), so a light veil satisfied it. **Fix, four wordings:** the canvas is "one continuous very wide panoramic painting"; the text third is calm open environment "painted at exactly the same colour depth, saturation, contrast, exposure, and finish as the rest of the picture", with lighten/fade/veil/haze/wash/whiten/blur/desaturate forbidden by name; the centre is no longer a named zone — it is "ordinary painting" with a content rule (no face, hand, or critical detail at or near the horizontal middle) and a treatment rule (no edges, boundaries, or change of treatment of its own); and the constraints ban the defect symmetrically (pale strip, milky or whitened band, veil, wash, fog, overlay) and state the acceptance test outright: the left and right halves must match in brightness, colour, contrast, and finish.
- **Observed defect: the interpolating centre gate declined every real veil in silence.** The gate repairs a 1–8 column band and must leave anything wider alone; the veil's soft shoulder is 9–16 columns wide and its body is half the canvas, so five of eight shipped spreads measured 11–57× baseline, were declined, and passed along with nothing logged. **Fix:** a decline is now logged, and a second deterministic reading — the **centre-field gate** (§ *Centre-field gate*) — refuses the picture instead of repairing it. It spends the same single regeneration the review ladder spends; a second refused base stops the spread as `IMAGE_GENERATION_FAILED`.

Nothing else moved: the identity lock, the anchor-first numbering, the outfit clause, the shot-first composition order, the 15:7 target, and both integration-zone percentages are v1.4's to the digit.

## v1.4 changelog

One amendment, from a contradiction the v1.3 wording created between this template and `minimal-visual-qa-v1.4`.

- **Observed defect: an unwinnable page when the parent's eye colour differs from the photograph.** v1.3 ended the identity lock with *"where this list and that photograph disagree, follow the photograph"* — a single authority, applied to every attribute. But the eye colour and the age are the parent's own entered values and beat the photograph by the same decision that made CHILD_AGE advisory. So a parent entering green eyes for a child whose photograph reads brown had the illustrator told to follow the photograph and the reviewer told to fail anything whose eyes were not green: the page is refused, redrawn from the same instruction, refused again, and the book stops. Neither model is wrong — they were given contradictory orders. **Fix:** the deference is scoped. The photograph settles WHO the child is; the entered values win where they differ.

```text
Image {{IDENTITY_PHOTO_IMAGE_NUMBER}} is the identity reference photograph and settles who this child is - the face and the likeness - wherever it and this list disagree about that; the eye colour and the age above are the parent's own entered values and win over the photograph wherever they differ.
```

Nothing else moved: the eight attributes, the anchor-first numbering, the outfit clause, the composition resolver and the centre-column gate are v1.3's.

## v1.3 changelog

Amended after the supplier's PDF audit of a shipped book (2026-08-30) reported that the wide, action and close spreads all render as similar medium compositions.

- **Observed defect: the shot instruction was injected but weakly positioned.** `{{SHOT_INSTRUCTION}}` is the only line that distinguishes spread 3's wide establishing view from spread 6's close one, and it sat *second* in the `COMPOSITION` block, behind "Create one continuous very wide panoramic two-page spread designed for a final 15:7 crop." A model that has just been told "very wide panoramic" has chosen its camera before it reaches the shot. **Fix:** the shot instruction is now the **first line of the composition block** and the panorama sentence follows it. Both sentences are unchanged, character for character; only their order moved. The shot wording itself still comes from `pipeline_config_v1.json`'s `spread_rhythm` — this template does not restate it.
- **Advisory, not a gate.** The minimal visual QA reviewer is now asked to record a free-text `shot_note` when the rendered shot clearly contradicts the described shot type (see `BEKI_Minimal_Visual_QA_v1.md` v1.3). It creates **no** new failed-check category, cannot fail a page, and cannot cause a retry — the false-positive risk on a subjective judgement is too high to spend a paid image call on. It exists so the next revision of this line has evidence instead of impressions.

Nothing else moved: the section order, the scene/outfit/identity/recurring blocks, the 15:7 target, the composition resolver's geometry and percentages, the constraint list and the retry ladder are v1.2's.

## v1.3 changelog

Three amendments from one refused image (pack `7fc8faf4`) and the owner's decision of 2026-08-30 that followed it.

- **Precedence, stated in the prompt: the photograph is WHO, the entered fields are HOW OLD and WHAT COLOUR.** The image had been refused for the child reading older than the entered age, and the prompt had never said which of the two to believe — it asked for "visibly age-appropriate proportions" and attached a photograph, leaving the model to reconcile them. It cannot: the photograph may be a year old, and the parent may be buying the book for a younger age deliberately. The child reference line and the identity lock's age line now say it outright — *render the child's proportions and face at {age} years old, which is the age this book is for, even if the photograph appears older or younger*. The matching QA gate came off in `minimal-visual-qa-v1.4`.
- **The reserved text third is scene, not a panel.** The same refused image rendered the reserved third as a flat blank field with a hard vertical boundary down its inner edge — while the constraint list forbids a "blank rectangle". The resolver was inviting what the constraints banned: "naturally calm, light background" reads as *paint a light background there*. It now asks for the same scene continued through that third as soft distant environment, gently lightening toward the outer edge, with no hard vertical boundary and no flat field of colour. Geometry and percentages are unchanged; this is wording only, and no new QA check was added for it.
- **The centre-column gate looks wider.** That image also carried a visible vertical band at about 52.5% of the width — outside the exact-centre window the gate had been scanning, so it reported nothing. The band is now four per cent of the width either side of centre, a repaired run may be up to eight columns, and the offset from centre is logged. The 5×-over-median threshold, the outermost-boundary pairing and the leave-alone rules for single edges and wide structures are unchanged, and the run is paired within a seam's width of the peak so that an ordinary edge inside the band cannot be dragged into a repair.

## v1.2 changelog

Amended after the owner's own review of the first books drawn under v1.1. Those books were internally consistent — spreads 1-6 of run `09d57d46` show the same child — and still wrong in ways v1.1 had no words for. The owner's verdict, in full: *"not the cloth not the face not the hair not the eyebrows not the glasses"*, and *"eye color gets fucked up almost always especially on the cover"*.

- **Observed defect: the lock was too narrow.** Four attributes named hair, eyes and skin, and said nothing about the shape of the face, the eyebrows above the eyes, or whether the child wears glasses — so each spread decided those afresh and a child could gain or lose glasses between pages. **Fix:** the spec is eight attributes (§ *Child identity spec*), taken from the supplier's own `character-identity-analyzer-v1` field set: face shape, eyebrows, glasses and three-to-five distinctive details join the original four. `glasses` is **required and must be stated even when the answer is "none"** — a field a model may leave blank is a field it decides page by page.
- **Observed defect: the anchor was attached third.** v1.1 put the child photograph first and the appearance anchor behind it and the world reference. An image model weights the first reference hardest, so every spread re-stylized a photograph of a real child while the picture that already *was* the book's answer sat third and read as a hint. **Fix:** on spreads 2-8 the accepted spread-1 base is **Image 1**, and the instruction asks for reproduction rather than resemblance. The photograph moves to Image 2 and is still attached on every call — it is the identity ground truth the anchor is answerable to, since an anchor that came out slightly wrong must not become the book's definition of the child.
- **Observed defect: the outfit drifted inside its own description.** The Visual Scenario's outfit sentence describes clothes in words, and the same words came out as a different mustard and a different collar. **Fix:** on the anchored spreads the outfit lock adds *"Draw the outfit exactly as rendered in Image 1."*
- **Observed defect: a faint centre line survives the de-folding.** v1.1 removed every mention of a fold and the painted band mostly went with it, but a faint discontinuity still appears at the exact centre of some bases. A prompt is a request; a picture that ships is a fact. **Fix:** a deterministic centre-column gate runs on every generated base and on the redrawn cover, before review and before anything is stored (§ *Centre-column gate*). No model call is involved.

Nothing else moved: the section order, the scene block, the shot instruction, the 15:7 target, the composition resolver's geometry and percentages, the constraint list and the retry ladder are v1.1's.

## v1.1 changelog

Amended under the handoff's own change rule — *only observed defects become the next implementation rules* — after the first real books were run on 2026-08-29.

- **Observed defect: painted centre seam.** The generated cover and spread bases carried a full-height dark band at the exact centre of the canvas (column-brightness jump 35× baseline on the newest cover; 37× on `spread-01-base.png`), in raw model output, with no stitching code anywhere in the pipeline. Cause: this template asserted that a fold exists — "away from the center fold", "between the center fold and the child", "Keep the center-fold zone low-information", "may cross or touch the fold zone" — before any negative forbade painting one. A model told four times that there is a fold there paints one. **Fix:** every naming of a fold, gutter, seam or binding is gone from this template. The same geometry is now named as the *central low-information zone*: the exclusion strip, its position and both integration-zone percentages are unchanged to the digit. The negative is rewritten to describe the defect without naming the thing that causes it.
- **Observed defect: child identity drift.** A completed composite book whose every spread passed minimal visual QA showed a visibly different child from page to page, and lost the eye colour entirely. Cause: identity rode only on the attached photograph, so each spread was an independent stylization of it; the prompt carried no identity attributes at all, and the one image reference that *is* a drawn child — the continuity reference — explicitly forbids copying the child. **Fix:** two additions. A `CHILD IDENTITY LOCK` block carries the four per-book attributes derived once from the photograph (§ *Child identity spec*), and every spread after the first is additionally shown the accepted spread-1 base as a **child appearance anchor**. The photograph stays the stated identity authority; the anchor fixes the stylization.

Nothing else moved: the section order, the scene/outfit/recurring blocks, the shot instruction, the 15:7 target, the constraint list's order and the retry rule are v1's.

## Runtime inputs

- `child_photo`: the original, consented child identity reference;
- `theme_reference`: one approved image selected by `theme_id`;
- `child_age_years`: numeric age from the application input;
- `child_identity_spec`: the eight per-book identity attributes — `hair_color`, `hair_style`, `eye_color`, `skin_tone`, `eyebrows`, `glasses`, `face_shape`, `distinctive_features` (§ *Child identity spec*);
- `child_world_scene`: the current Visual Scenario v2 scene;
- `child_outfit`: the book-level Visual Scenario v2 outfit lock;
- `relevant_recurring_elements`: only recurring elements required on this image;
- `text_side`: deterministic `LEFT` or `RIGHT` from the page rhythm;
- `shot_instruction`: deterministic instruction from `pipeline_config_v1.json`;
- `child_appearance_anchor`: the final QA-accepted spread-1 base image, on every spread after the first;
- `continuity_reference`: optional most recent approved image containing a recurring story character or object.

Do not attach a Beki image to this call. The child photo is an identity reference only. The theme image is a world/style reference only. The child appearance anchor is used only for the child's stylized appearance. The optional continuity image is used only for the named recurring story elements. At most four images are sent on any one call.

## Child identity spec

Derived once per book, before the first spread is generated, by asking the configured vision model to read the consented child photo and return exactly:

```json
{
  "hair_color": "…", "hair_style": "…", "eye_color": "…", "skin_tone": "…",
  "eyebrows": "…", "glasses": "none | …", "face_shape": "…", "distinctive_features": "…; …; …"
}
```

Rules:

- each value is a short, plain, neutral phrase — no names, no ethnicity labels, no judgements, no sentences;
- `glasses` is **required** and is written as exactly `none` when the child wears none, or a few words describing the frames when they do. It is never blank and never omitted: the field exists so that the illustrator is told about glasses on every page, whichever way the answer goes;
- `distinctive_features` carries three to five short details separated by semicolons — freckles, a dimple, a gap in the front teeth — which is what makes a stylization recognisably *this* child rather than a child of that colouring;
- the answer is schema-validated; one corrective retry is permitted, and a second invalid answer stops the book with `IDENTITY_SPEC_FAILED`. There is no soft-degrade: an all-pass book that drifted is what proved photo-only identity insufficient;
- a parent-supplied eye colour, where the application already holds one, replaces the derived `eye_color`;
- the spec is per-book private data. It is persisted with the pack's own state, alongside the photograph, and **never appears in a log or telemetry record**. Logs carry the event, the prompt version and a hash of the spec, and nothing else.

## Composition resolver

Resolve the following block in application code before sending the prompt.

### When `text_side = LEFT`

```text
Keep the full left third quiet enough to set story text over: continue the same scene through it as calm open environment - sky, far foliage, open ground - painted at exactly the same colour depth, saturation, contrast, exposure, and finish as the rest of the picture. It is calm because the scene is calm there, not because anything covers it: do not lighten it, and do not fade, veil, haze over, wash, whiten, blur, or desaturate it or any other region of the painting. It is part of the painting, not a panel: there must be no hard vertical boundary where it begins, no flat field of colour, no visible edge between it and the rest of the picture, and no change of tone marking where it begins or ends. No character, face, hand, foreground object, or key action may enter this area. Place the child and the main action in the outer-right area. Leave a naturally lit, visually quiet Beki integration zone centered approximately at 59.4% of the canvas width and 45.8% of the canvas height. Keep that zone free of characters, faces, hands, hard edges, foreground objects, and story-critical details.
```

### When `text_side = RIGHT`

```text
Keep the full right third quiet enough to set story text over: continue the same scene through it as calm open environment - sky, far foliage, open ground - painted at exactly the same colour depth, saturation, contrast, exposure, and finish as the rest of the picture. It is calm because the scene is calm there, not because anything covers it: do not lighten it, and do not fade, veil, haze over, wash, whiten, blur, or desaturate it or any other region of the painting. It is part of the painting, not a panel: there must be no hard vertical boundary where it begins, no flat field of colour, no visible edge between it and the rest of the picture, and no change of tone marking where it begins or ends. No character, face, hand, foreground object, or key action may enter this area. Place the child and the main action in the outer-left area. Leave a naturally lit, visually quiet Beki integration zone centered approximately at 40.6% of the canvas width and 45.8% of the canvas height. Keep that zone free of characters, faces, hands, hard edges, foreground objects, and story-critical details.
```

## Exact runtime prompt template

```text
Use case: illustration-story
Asset type: BEKI personalized children's book child/world base image for later exact Beki PNG compositing

INPUT IMAGES
{{NUMBERED_INPUT_IMAGE_BLOCK}}

SCENE
{{CHILD_WORLD_SCENE}}
Show this as one clear visible moment only.

CHILD LOCK
Dress the child in {{CHILD_OUTFIT}}{{OUTFIT_ANCHOR_CLAUSE}}
Keep the outfit consistent with the cover and all other story spreads. Do not hide the child's face.

CHILD IDENTITY LOCK
Face shape: {{FACE_SHAPE}}
Hair colour: {{HAIR_COLOR}}
Hair style: {{HAIR_STYLE}}
Eyebrows: {{EYEBROWS}}
Eye colour: {{EYE_COLOR}}
Skin tone: {{SKIN_TONE}}
Glasses: {{GLASSES}}
Distinctive features: {{DISTINCTIVE_FEATURES}}
The child is {{CHILD_AGE_YEARS}} years old in this book. That is the age the parent entered, and it is the age to draw: the photograph says who the child is, not how old they are here, and it may have been taken some time ago.
These attributes are identical on the cover and on all eight spreads. The child's eyes are {{EYE_COLOR}} on every page. Image {{IDENTITY_PHOTO_IMAGE_NUMBER}} is the identity reference photograph and settles who this child is - the face and the likeness - wherever it and this list disagree about that; the eye colour and the age above are the parent's own entered values and win over the photograph wherever they differ.

RECURRING ELEMENTS REQUIRED ON THIS IMAGE
{{RELEVANT_RECURRING_ELEMENTS_OR_NONE}}

COMPOSITION
{{SHOT_INSTRUCTION}}
Create one continuous very wide panoramic painting designed for a final 15:7 crop.
{{RESOLVED_TEXT_AND_BEKI_ZONE_BLOCK}}
The middle of the canvas is ordinary painting: continue the environment through it with the same light, the same colour, and the same level of detail as its surroundings, and give it no edges, boundaries, or change of treatment of its own. No face, hand, child, supporting character, or story-critical detail may sit at or near the horizontal middle of the picture.
Keep all important content in the central horizontal band so modest top-and-bottom crop normalization is safe.

STYLE AND MOOD
Premium warm stylized 3D children's-book illustration; expressive but natural; soft tactile materials; cinematic depth; welcoming, age-appropriate emotional tone. Match the supplied approved theme reference while creating a new scene.

HARD CONSTRAINTS
Exactly one child.
Do not generate Beki.
Do not generate any substitute guide, floating mascot, leaf spirit, lamb, sheep, or Beki-like character.
Do not generate characters or objects not required by the current scene.
No duplicate child or duplicated supporting character.
No text, letters, numbers, logos, captions, labels, signs, frames, QR codes, watermarks, or pseudo-text anywhere.
The picture is one continuous unbroken painting: no visible vertical dividing line, crease, shadow band, dark strip, pale strip, milky or whitened band, page edge, border, or split down the middle. Paint the environment straight through the centre of the canvas as if it were any other part of the scene.
The left and right halves of the picture must match in brightness, colour, contrast, and finish: neither half may be lighter, paler, hazier, more faded, or more washed-out than the other, and no veil, wash, fog, or overlay may cover any part of the painting.
No split screen, montage, comic panel, inset frame, before-and-after view, or repeated version of the same character.
No dark or pale text panel, milky veil, white or cream overlay, artificial blur panel, or blank rectangle. The text-safe area is ordinary full-colour painting like the rest of the scene.
```

## Numbered input images

The images are numbered by the order they are actually attached, and the prompt's numbering must match the request exactly — a prompt that names Image 1 as the anchor while the request attaches the photograph first tells the model to take the child's face from a photograph it was told to treat as a drawing. Two shapes exist and both occur in every book.

**Spread 1 — no anchor yet, because this is the page that makes it:**

```text
Image 1 - child identity reference photograph. Preserve the child's recognizable identity: this photograph says WHO the child is, and nothing else. Render the child's proportions and face at {{CHILD_AGE_YEARS}} years old, which is the age this book is for, even if the photograph appears older or younger - it may have been taken some time ago. Render the child as a warm, polished stylized 3D animated character, not photorealistically. Do not copy clothing, pose, lighting, crop, or background from the photo.
Image 2 - approved {{THEME_ID}} world/style reference. …
Image 3 - continuity reference. …            (only when one is attached)
```

**Spreads 2-8 — the accepted spread-1 base leads:**

```text
Image 1 - child appearance anchor - the accepted first spread of this same book. Reproduce this exact rendered child: same face and face shape, same hair colour and style, same eyebrows, same glasses or absence of glasses, same eye colour, same skin tone, same outfit down to its colours. Give the child a new pose, camera angle and background as this page's scene requires. Do not copy the pose, camera, layout, lighting or background from this image.
Image 2 - child identity reference photograph. … This photograph is the identity ground truth: Image 1 shows how this child has already been drawn, and where the two disagree about who the child is, the photograph is right.
Image 3 - approved {{THEME_ID}} world/style reference. …
Image 4 - continuity reference. …            (only when one is attached)
```

The anchor is the **final QA-accepted** spread-1 base, which on a page that needed its one regeneration is the regenerated base and never the refused draft. The photograph is attached on every call in both shapes; it is never dropped in favour of the anchor. At most four images are sent.

`{{OUTFIT_ANCHOR_CLAUSE}}` is ` Draw the outfit exactly as rendered in Image 1.` on the anchored spreads and an empty string on spread 1. `{{IDENTITY_PHOTO_IMAGE_NUMBER}}` is 2 on the anchored spreads and 1 on spread 1 — it must name the photograph's actual position, or the lock defers to the wrong picture.

## Centre-column gate

Deterministic, arithmetic, and applied to every generated base and to the redrawn cover before the reviewer or the compositor sees one:

1. measure the mean absolute difference between each adjacent pair of columns, over every row and the three colour channels;
2. take the **median** of those differences outside a band of **four per cent of the width either side of the exact centre** as the picture's baseline — a median, because a scene with a few hard vertical edges would otherwise hide a seam behind its own strongest features;
3. take the largest difference inside that band as the centre reading, and record how far from the centre it sits;
4. when the centre reading is more than **5×** the baseline, replace the offending run of **1 to 8 columns** with a straight linear interpolation between the intact columns on either side, row by row, and measure again;
5. log both readings.

The threshold sits between a picture's ordinary variation (about 1×) and the measured defect (35-68× before the v1.1 de-folding). A run wider than eight columns is not a seam and is left alone: the cost of a missed seam is a faint line, and the cost of a false positive is four smeared columns of somebody's artwork. **v1.5:** a measurement above threshold with no repairable run — a hard step, or a band wider than eight columns — is logged and handed to the centre-field gate below rather than silently passed.

## Centre-field gate (v1.5)

Deterministic, arithmetic, applied to every normalized base immediately after the centre-column gate and before the compositor or the reviewer sees one. It repairs nothing; it refuses. This is the audit's own acceptance test — "compare narrow image strips immediately to the left and right of the centre and reject a centre-aligned discontinuity affecting most of the image height" — plus a second reading for the veil whose soft shoulder no strip comparison at the boundary can see:

1. **Edge reading.** For every candidate boundary within four per cent of the width either side of centre, compare the 8-column strips immediately left and right of it, row by row; a row counts when they differ by more than 12 luma. The reading is the largest fraction of counting rows over any candidate. **Limit 40%.** A razor seam scores most rows; the worst clean reference (the supplier's approved fixture base) scores 27%.
2. **Field reading.** Same candidates, but compare 48-column strips starting 12 columns out on either side — past the veil's soft shoulder — and count a row only in its dominant direction (left-lighter vs right-lighter, threshold 15 luma). The reading is the larger one-way fraction. **Limit 55%.** The sign discipline is what keeps a legitimately asymmetric composition safe: the approved fixture's halves differ by 52 luma yet score 43% because its differences point both ways, while a veil lifts one whole side and scores 56-100%.
3. Calibration: the thirty-nine veiled bases stored from six real runs all exceed at least one limit; the supplier's approved fixture base and all six approved intro backgrounds pass both.
4. A refused base spends the spread's single regeneration (the same budget the review ladder spends). A regenerated base that is refused again stops the spread with `IMAGE_GENERATION_FAILED`, naming both readings.

## Application checks before the call

- `child_world_scene` contains `the child` and does not contain `Beki`;
- `child_outfit` is non-empty;
- `child_identity_spec` is present and complete, including `glasses`; a book without one does not reach this call;
- `text_side` and `shot_instruction` come from code, not model output;
- the selected theme asset hash matches the registry;
- Image 1 and Image 2 are readable;
- the child appearance anchor is omitted on spread 1 and is the FIRST attached image on every later spread;
- the child photograph is attached on every call, whichever position it holds;
- the numbers written in the prompt are the positions the images are actually attached in;
- the centre-column gate and the centre-field gate have run on this base;
- the continuity image is omitted unless a continuity element is explicitly named;
- the images are numbered in the order they are attached, and no more than four are attached;
- no secret, signed URL, raw image bytes, identity-spec attribute value, or unrelated child data is inserted into the logged prompt record.

## Generation retry rule

The image stage gets one regeneration attempt per spread, and never more than one. A Beki placement failure is fixed by re-compositing at an adjusted anchor and must not, on its own, trigger a new image-model call.

v1.1 adds the one case where it may. A placement the reviewer refuses *twice* — once at the configured anchor and once at the adjusted one — is evidence about the picture rather than about the placement, so if the single regeneration has not already been spent, it is spent then, at the approved anchor. The bound is unchanged in the only way that matters: two generated base images per spread, never three. The ladder is written out in full in `BEKI_Minimal_Visual_QA_v1.md`.

v1.5 adds the second case: a base the centre-field gate refuses spends the same single regeneration, before any review is bought. The bound still holds — two generated base images per spread, never three — because the gate and the ladder draw from one budget, and a spread whose second base is also refused stops as `IMAGE_GENERATION_FAILED` without a further call of any kind.
