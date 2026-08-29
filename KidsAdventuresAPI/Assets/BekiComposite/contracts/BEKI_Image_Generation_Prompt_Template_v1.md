# BEKI Child/World Image Prompt Template v1

**Prompt version:** `child-world-image-v1`  
**Status:** Implementation source  
**Purpose:** Generate a text-free child-and-world base image. Beki is composited later from an approved transparent PNG.

## Runtime inputs

- `child_photo`: the original, consented child identity reference;
- `theme_reference`: one approved image selected by `theme_id`;
- `child_age_years`: numeric age from the application input;
- `child_world_scene`: the current Visual Scenario v2 scene;
- `child_outfit`: the book-level Visual Scenario v2 outfit lock;
- `relevant_recurring_elements`: only recurring elements required on this image;
- `text_side`: deterministic `LEFT` or `RIGHT` from the page rhythm;
- `shot_instruction`: deterministic instruction from `pipeline_config_v1.json`;
- `continuity_reference`: optional most recent approved image containing a recurring story character or object.

Do not attach a Beki image to this call. The child photo is an identity reference only. The theme image is a world/style reference only. The optional continuity image is used only for the named recurring story elements.

## Composition resolver

Resolve the following block in application code before sending the prompt.

### When `text_side = LEFT`

```text
Reserve the full left third as naturally calm, light background for later story text. No character, face, hand, foreground object, or key action may enter this area. Place the child and the main action in the outer-right area, away from the center fold. Leave a naturally lit, visually quiet Beki integration zone between the center fold and the child, centered approximately at 59.4% of the canvas width and 45.8% of the canvas height. Keep that zone free of characters, faces, hands, hard edges, foreground objects, and story-critical details.
```

### When `text_side = RIGHT`

```text
Reserve the full right third as naturally calm, light background for later story text. No character, face, hand, foreground object, or key action may enter this area. Place the child and the main action in the outer-left area, away from the center fold. Leave a naturally lit, visually quiet Beki integration zone between the child and the center fold, centered approximately at 40.6% of the canvas width and 45.8% of the canvas height. Keep that zone free of characters, faces, hands, hard edges, foreground objects, and story-critical details.
```

## Exact runtime prompt template

```text
Use case: illustration-story
Asset type: BEKI personalized children's book child/world base image for later exact Beki PNG compositing

INPUT IMAGES
Image 1 - child identity reference. Preserve the child's recognizable identity and visibly age-appropriate proportions for approximately {{CHILD_AGE_YEARS}} years old. Render the child as a warm, polished stylized 3D animated character, not photorealistically. Do not copy clothing, pose, lighting, crop, or background from the photo.
Image 2 - approved {{THEME_ID}} world/style reference. Use its world vocabulary, palette, atmosphere, material treatment, and premium stylized 3D rendering language. Create a new composition; do not copy the reference composition.
{{OPTIONAL_CONTINUITY_REFERENCE_INSTRUCTION}}

SCENE
{{CHILD_WORLD_SCENE}}
Show this as one clear visible moment only.

CHILD LOCK
Dress the child in {{CHILD_OUTFIT}}
Keep the outfit consistent with the cover and all other story spreads. Do not hide the child's face.

RECURRING ELEMENTS REQUIRED ON THIS IMAGE
{{RELEVANT_RECURRING_ELEMENTS_OR_NONE}}

COMPOSITION
Create one continuous very wide panoramic two-page spread designed for a final 15:7 crop.
{{SHOT_INSTRUCTION}}
{{RESOLVED_TEXT_AND_BEKI_ZONE_BLOCK}}
Keep the center-fold zone low-information, with only continuous environment crossing it. No face, hand, child, supporting character, or story-critical detail may cross or touch the fold zone.
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
No split screen, montage, comic panel, inset frame, before-and-after view, or repeated version of the same character.
No dark text panel, artificial blur panel, or blank rectangle. The text-safe area must be part of the natural environment.
```

## Optional continuity-reference instruction

Use this only when an approved previous image is actually supplied:

```text
Image 3 - continuity reference. Preserve only the appearance of these named recurring story elements: {{CONTINUITY_ELEMENT_NAMES}}. Do not copy the child, Beki, pose, camera, layout, lighting, or background from this image.
```

Otherwise replace the placeholder with an empty string and do not mention a third image.

## Application checks before the call

- `child_world_scene` contains `the child` and does not contain `Beki`;
- `child_outfit` is non-empty;
- `text_side` and `shot_instruction` come from code, not model output;
- the selected theme asset hash matches the registry;
- Image 1 and Image 2 are readable;
- Image 3 is omitted unless a continuity element is explicitly named;
- no secret, signed URL, raw image bytes, or unrelated child data is inserted into the logged prompt record.

## Generation retry rule

The image stage gets one regeneration attempt only after a critical QA failure. A Beki placement failure is fixed by re-compositing and must not trigger a new image-model call.
