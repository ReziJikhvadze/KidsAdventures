# BEKI Cover Base Prompt Template v1

**Prompt version:** `cover-child-world-v1`  
**Status:** Implementation source with printer-geometry placeholders  
**Purpose:** Generate one continuous cover base without Beki, title text, spine text, QR, or other typography.

## Required geometry from the existing cover composer

The application must resolve these values from the active printer-approved cover configuration before a model call:

- full raster canvas aspect ratio and target size;
- back-panel rectangle;
- spine rectangle and hinge/safe zones;
- front-panel rectangle;
- front title-safe rectangle;
- front child/action rectangle;
- front Beki integration rectangle;
- outer wrap/bleed and board/trim safety.

The audited benchmark has a 512 x 245 mm MediaBox/BleedBox and a 472 x 205 mm TrimBox, but these values remain printer-specific and must not be hard-coded as a universal product rule. If the active cover geometry is unavailable, stop before generation and return `LAYOUT_FAILED`; do not substitute the interior 5 mm bleed.

## Runtime inputs

- original consented child identity photo;
- approved theme reference;
- `cover.front_child_world_scene`;
- `cover.back_environment`;
- `visual_lock.child_outfit`;
- relevant recurring elements required by the front scene;
- resolved natural-language panel/zone instructions from the existing cover geometry.

Do not send a Beki reference. `cover.beki_action` is used after generation by deterministic pose selection and exact-PNG compositing.

## Exact runtime prompt template

```text
Use case: illustration-cover
Asset type: BEKI personalized children's book continuous wraparound cover base for later vector title and exact Beki PNG compositing

INPUT IMAGES
Image 1 - child identity reference. Preserve the child's recognizable identity and visibly age-appropriate proportions for approximately {{CHILD_AGE_YEARS}} years old. Render the child as a warm, polished stylized 3D animated character, not photorealistically. Do not copy clothing, pose, lighting, crop, or background from the photo.
Image 2 - approved {{THEME_ID}} world/style reference. Use its world vocabulary, palette, atmosphere, material treatment, and premium stylized 3D rendering language. Create a new cover composition.

FRONT-COVER SCENE
{{FRONT_CHILD_WORLD_SCENE}}
Dress the child in {{CHILD_OUTFIT}}
Show one inviting action only. Do not reveal the ending.

BACK-COVER ENVIRONMENT
{{BACK_ENVIRONMENT}}
Continue the same world, terrain, atmosphere, and lighting naturally across the complete wrap. The back panel contains no child, Beki, or other main character unless the approved product specification explicitly requires one.

RECURRING ELEMENTS REQUIRED ON THE FRONT
{{RELEVANT_RECURRING_ELEMENTS_OR_NONE}}

PRINTER-SPECIFIC COMPOSITION
{{RESOLVED_COVER_PANEL_AND_SAFE_ZONE_INSTRUCTIONS}}
Keep the spine and hinge zones low-information and continuous. No face, hand, child, supporting character, title-critical feature, or story-critical object may enter them.
Keep the front title-safe rectangle naturally calm and readable without using a blank panel, artificial blur, dark rectangle, or hard-edged box.
Keep the front Beki integration rectangle naturally lit and clear of characters, faces, hands, hard foreground edges, and story-critical details.
Extend the environment safely through every required wrap/bleed edge.

STYLE AND MOOD
Premium warm stylized 3D children's-book cover; expressive but natural; soft tactile materials; cinematic depth; clear front-cover focal hierarchy; welcoming, age-appropriate adventure. Match the approved theme reference while creating a new scene.

HARD CONSTRAINTS
Exactly one child, on the front panel only.
Do not generate Beki.
Do not generate any substitute guide, floating mascot, leaf spirit, lamb, sheep, or Beki-like character.
No duplicate child or mirrored second child.
No text, title, letters, numbers, logo, caption, label, sign, spine text, QR code, watermark, or pseudo-text anywhere.
No split screen, montage, comic panel, inset frame, or mirrored composition.
```

## Deterministic post-generation steps

1. Validate panel orientation and safe zones against the active dieline.
2. Select an approved Beki pose from `cover.beki_action`.
3. Composite the exact approved pose inside the configured front Beki rectangle and record a manifest.
4. Add the Ottia title and any other cover copy as vector layers through the existing cover composer.
5. Apply the active dieline, printer boxes, color conversion, and preflight after all layers are final.

No cover anchor is invented in this contract because the active printer cover geometry is an external required input. The developer must store the approved front-panel Beki anchor in cover configuration and return it in the first-run manifest.
