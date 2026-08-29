# BEKI Child/World Image Prompt Template v1.1

**Prompt version:** `child-world-image-v1.1`  
**Status:** Implementation source  
**Purpose:** Generate a text-free child-and-world base image. Beki is composited later from an approved transparent PNG.

## v1.1 changelog

Amended under the handoff's own change rule — *only observed defects become the next implementation rules* — after the first real books were run on 2026-08-29.

- **Observed defect: painted centre seam.** The generated cover and spread bases carried a full-height dark band at the exact centre of the canvas (column-brightness jump 35× baseline on the newest cover; 37× on `spread-01-base.png`), in raw model output, with no stitching code anywhere in the pipeline. Cause: this template asserted that a fold exists — "away from the center fold", "between the center fold and the child", "Keep the center-fold zone low-information", "may cross or touch the fold zone" — before any negative forbade painting one. A model told four times that there is a fold there paints one. **Fix:** every naming of a fold, gutter, seam or binding is gone from this template. The same geometry is now named as the *central low-information zone*: the exclusion strip, its position and both integration-zone percentages are unchanged to the digit. The negative is rewritten to describe the defect without naming the thing that causes it.
- **Observed defect: child identity drift.** A completed composite book whose every spread passed minimal visual QA showed a visibly different child from page to page, and lost the eye colour entirely. Cause: identity rode only on the attached photograph, so each spread was an independent stylization of it; the prompt carried no identity attributes at all, and the one image reference that *is* a drawn child — the continuity reference — explicitly forbids copying the child. **Fix:** two additions. A `CHILD IDENTITY LOCK` block carries the four per-book attributes derived once from the photograph (§ *Child identity spec*), and every spread after the first is additionally shown the accepted spread-1 base as a **child appearance anchor**. The photograph stays the stated identity authority; the anchor fixes the stylization.

Nothing else moved: the section order, the scene/outfit/recurring blocks, the shot instruction, the 15:7 target, the constraint list's order and the retry rule are v1's.

## Runtime inputs

- `child_photo`: the original, consented child identity reference;
- `theme_reference`: one approved image selected by `theme_id`;
- `child_age_years`: numeric age from the application input;
- `child_identity_spec`: the four per-book identity attributes — `hair_color`, `hair_style`, `eye_color`, `skin_tone` (§ *Child identity spec*);
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
{"hair_color": "…", "hair_style": "…", "eye_color": "…", "skin_tone": "…"}
```

Rules:

- each value is a short, plain, neutral phrase — no names, no ethnicity labels, no judgements, no sentences;
- the answer is schema-validated; one corrective retry is permitted, and a second invalid answer stops the book with `IDENTITY_SPEC_FAILED`. There is no soft-degrade: an all-pass book that drifted is what proved photo-only identity insufficient;
- a parent-supplied eye colour, where the application already holds one, replaces the derived `eye_color`;
- the spec is per-book private data. It is persisted with the pack's own state, alongside the photograph, and **never appears in a log or telemetry record**. Logs carry the event, the prompt version and a hash of the spec, and nothing else.

## Composition resolver

Resolve the following block in application code before sending the prompt.

### When `text_side = LEFT`

```text
Reserve the full left third as naturally calm, light background for later story text. No character, face, hand, foreground object, or key action may enter this area. Place the child and the main action in the outer-right area, away from the central low-information zone. Leave a naturally lit, visually quiet Beki integration zone between the central low-information zone and the child, centered approximately at 59.4% of the canvas width and 45.8% of the canvas height. Keep that zone free of characters, faces, hands, hard edges, foreground objects, and story-critical details.
```

### When `text_side = RIGHT`

```text
Reserve the full right third as naturally calm, light background for later story text. No character, face, hand, foreground object, or key action may enter this area. Place the child and the main action in the outer-left area, away from the central low-information zone. Leave a naturally lit, visually quiet Beki integration zone between the child and the central low-information zone, centered approximately at 40.6% of the canvas width and 45.8% of the canvas height. Keep that zone free of characters, faces, hands, hard edges, foreground objects, and story-critical details.
```

## Exact runtime prompt template

```text
Use case: illustration-story
Asset type: BEKI personalized children's book child/world base image for later exact Beki PNG compositing

INPUT IMAGES
Image 1 - child identity reference. Preserve the child's recognizable identity and visibly age-appropriate proportions for approximately {{CHILD_AGE_YEARS}} years old. Render the child as a warm, polished stylized 3D animated character, not photorealistically. Do not copy clothing, pose, lighting, crop, or background from the photo.
Image 2 - approved {{THEME_ID}} world/style reference. Use its world vocabulary, palette, atmosphere, material treatment, and premium stylized 3D rendering language. Create a new composition; do not copy the reference composition.
{{OPTIONAL_CHILD_APPEARANCE_ANCHOR_INSTRUCTION}}
{{OPTIONAL_CONTINUITY_REFERENCE_INSTRUCTION}}

SCENE
{{CHILD_WORLD_SCENE}}
Show this as one clear visible moment only.

CHILD LOCK
Dress the child in {{CHILD_OUTFIT}}
Keep the outfit consistent with the cover and all other story spreads. Do not hide the child's face.

CHILD IDENTITY LOCK
Hair colour: {{HAIR_COLOR}}
Hair style: {{HAIR_STYLE}}
Eye colour: {{EYE_COLOR}}
Skin tone: {{SKIN_TONE}}
The child is approximately {{CHILD_AGE_YEARS}} years old.
These attributes are identical on the cover and on all eight spreads. Image 1 remains the identity authority; where this list and Image 1 disagree, follow Image 1.

RECURRING ELEMENTS REQUIRED ON THIS IMAGE
{{RELEVANT_RECURRING_ELEMENTS_OR_NONE}}

COMPOSITION
Create one continuous very wide panoramic two-page spread designed for a final 15:7 crop.
{{SHOT_INSTRUCTION}}
{{RESOLVED_TEXT_AND_BEKI_ZONE_BLOCK}}
Keep the narrow vertical strip at the exact centre of the canvas as a central low-information zone, with only continuous environment passing through it. No face, hand, child, supporting character, or story-critical detail may cross or touch that central zone.
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
The picture is one continuous unbroken painting: no visible vertical dividing line, crease, shadow band, dark strip, page edge, border, or split down the middle. Paint the environment straight through the centre of the canvas as if it were any other part of the scene.
No split screen, montage, comic panel, inset frame, before-and-after view, or repeated version of the same character.
No dark text panel, artificial blur panel, or blank rectangle. The text-safe area must be part of the natural environment.
```

## Optional child appearance anchor instruction

Attached on every spread after the first, and never on the first — the first spread is what produces it. The anchor is the **final QA-accepted** spread-1 base image, which on a page that needed its one regeneration is the regenerated base and never the refused draft.

```text
Image 3 - child appearance anchor. Match this exact stylized child - same face, hair style and colour, eye colour, skin tone, outfit. Do not copy pose, camera, layout, or background from this image. The child photo (Image 1) remains the identity authority; the anchor fixes the stylization.
```

Otherwise replace the placeholder with an empty string and do not mention this image.

## Optional continuity-reference instruction

Use this only when an approved previous image is actually supplied. It keeps its v1 role and its "do not copy the child" clause, and is renumbered when the anchor is also attached.

**With an anchor attached (spreads 2-8):**

```text
Image 4 - continuity reference. Preserve only the appearance of these named recurring story elements: {{CONTINUITY_ELEMENT_NAMES}}. Do not copy the child, Beki, pose, camera, layout, lighting, or background from this image.
```

**With no anchor attached (spread 1):**

```text
Image 3 - continuity reference. Preserve only the appearance of these named recurring story elements: {{CONTINUITY_ELEMENT_NAMES}}. Do not copy the child, Beki, pose, camera, layout, lighting, or background from this image.
```

Otherwise replace the placeholder with an empty string and do not mention a further image.

## Application checks before the call

- `child_world_scene` contains `the child` and does not contain `Beki`;
- `child_outfit` is non-empty;
- `child_identity_spec` is present and complete; a book without one does not reach this call;
- `text_side` and `shot_instruction` come from code, not model output;
- the selected theme asset hash matches the registry;
- Image 1 and Image 2 are readable;
- the child appearance anchor is omitted on spread 1 and present on every later spread;
- the continuity image is omitted unless a continuity element is explicitly named;
- the images are numbered in the order they are attached, and no more than four are attached;
- no secret, signed URL, raw image bytes, identity-spec attribute value, or unrelated child data is inserted into the logged prompt record.

## Generation retry rule

The image stage gets one regeneration attempt only after a critical QA failure. A Beki placement failure is fixed by re-compositing and must not trigger a new image-model call.
