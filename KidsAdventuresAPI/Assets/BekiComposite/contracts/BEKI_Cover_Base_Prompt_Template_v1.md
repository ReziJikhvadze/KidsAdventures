# BEKI Cover Base Prompt Template v1.1

**Prompt version:** `cover-child-world-v1.1`  
**Status:** Implementation source with printer-geometry placeholders  
**Purpose:** Generate one continuous cover base without Beki, title text, spine text, QR, or other typography.

## v1.1 changelog

Amended against the supplier's audit of 2026-08-31, finding **P0-03 — "Visible cover construction bands are baked into the artwork"**: `cover-wrap-base.png` carried strong vertical tonal jumps at x=1236 and x=1291 px, which on a 2528 px-wide 512 mm cover are 250.5 mm and 261.5 mm — the spine boundaries to the tenth of a millimetre — plus an abrupt warm-green-to-purple change of world across them.

- **Observed defect: the prompt named the construction and the model painted it.** The resolved panel block handed the model the dieline as percentages — *"Centre construction: from 47% to 53% of the canvas width"*, *"Back panel: from 4% to 47%"*, *"Front panel: from 53% to 96%"*, a title area bounded at *"14% to 33%"* — and then asked for those regions to be kept low-information. A region with numbers is a region with edges; the model drew the edges. **Fix:** the block is painter's language about one picture. The subject is on the right side, the left side is quieter environment with no child and no second version of the composition, the middle of the picture stays simple, calm and low in detail with the same light and colour as its surroundings, the upper right stays naturally calm and open, and the world runs off every outer edge with nothing important near them. No percentage, no zone, no boundary, no panel and no "construction" is named anywhere. The spine, the hinge and the Beki integration rectangle are not mentioned at all: they are where the wrap is cut, where the pose is composited and where the title is typeset — deterministic work that happens after generation and needs no word in the prompt.
- **The reifiable-region law, now measured three times.** This is the third incident of one failure family in this pipeline, and the third time the fix has been to stop naming the place: v1.1 of the child/world template removed the word *fold* after the first books came back with a dark band painted down the middle at 35× the baseline column step; v1.6 removed the *"Beki integration zone"* after a live page came back with a translucent rectangle whose left edge sat at exactly 40.6% of the width, the precise coordinate the sentence gave; and v1.1 here removes the panel percentages after the spine bands. **The rule this contract now holds: a region an image prompt names is a region the image model may paint. Geometry belongs to code, never to the prompt.**
- **The negatives ban the defect as it actually appeared.** The old constraint list forbade drawn things only — a fold line, a crease, a seam, a gutter shadow — and told the model outright that *"the fold is where the printed book will be bound"*, naming the very thing it must not draw. A *tonal* discontinuity satisfied every word of it. The list now bans a vertical step in tone, colour, temperature or light anywhere in the picture and states the acceptance test the cover-band gate takes at the four dieline lines (242.5, 250.5, 261.5, 269.5 mm): the two sides of the painting must match in brightness, colour, contrast and finish. "Spine text" also leaves the no-text line — that line already says "anywhere", and the only thing naming the spine there could add is the idea of a spine.

Nothing else moved: the input images, the front/back scene structure, the recurring-elements block, the style-and-mood paragraph and the exact-Beki prohibition are v1's word for word.

## Required geometry from the existing cover composer

The application must resolve these values from the active printer-approved cover configuration before a model call. Since v1.1 they are resolved **for code** — cropping the panorama to the wrap, compositing the approved pose, typesetting the title, and measuring the result — and **none of them is stated to the model as a number, a rectangle, or a named region**:

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
- the resolved composition block — natural-language painting direction derived from the active cover geometry, carrying no coordinate, percentage, rectangle, or region name (v1.1).

### The resolved composition block, as the active dieline resolves it

`BekiCoverDieline.PanelInstructions` is this text verbatim. It is the whole of what the geometry says to the model:

```text
This is one continuous panoramic scene, painted as a single picture from edge to edge.
The child and the one inviting story action belong on the right side of the picture.
The left side is the same world continuing outward as quieter environment: no child, no other character, and no story action there, and never a second version of the composition on the right.
Through the middle of the picture the scene stays simple, calm, and low in detail — open sky, far ground, quiet water or foliage — carrying the same light, colour, and finish as everything around it, with nothing marked, tinted, framed, blurred, or edged there and no face, hand, character, or story-critical detail sitting there.
The upper right of the picture stays naturally calm and open, readable without a blank panel, artificial blur, dark rectangle, or hard-edged box.
Let the scene run off all four outer edges naturally, and keep everything important well away from those edges.
```

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
Continue the same world, terrain, atmosphere, and lighting naturally across the whole picture. The left side of the picture contains no child, no Beki, and no other main character.

RECURRING ELEMENTS REQUIRED ON THE FRONT
{{RELEVANT_RECURRING_ELEMENTS_OR_NONE}}

PRINTER-SPECIFIC COMPOSITION
{{RESOLVED_COVER_COMPOSITION_BLOCK}}
The middle of the picture is ordinary scene: continue the environment through it with the same light, the same colour, and the same level of detail as its surroundings, and give it no edge, band, tint, seam, or change of treatment of its own. No face, hand, child, supporting character, or story-critical object may sit at or near the horizontal middle of the picture.
Let the environment run all the way off every outer edge of the picture, and keep everything important well away from those edges.

STYLE AND MOOD
Premium warm stylized 3D children's-book cover; expressive but natural; soft tactile materials; cinematic depth; clear front-cover focal hierarchy; welcoming, age-appropriate adventure. Match the approved theme reference while creating a new scene.

HARD CONSTRAINTS
Exactly one child, on the right side of the picture only.
Do not generate Beki.
Do not generate any substitute guide, floating mascot, leaf spirit, lamb, sheep, or Beki-like character.
No duplicate child or mirrored second child.
No text, title, letters, numbers, logo, caption, label, sign, QR code, watermark, or pseudo-text anywhere.
The picture is one continuous unbroken painting: no visible dividing line, crease, seam, shadow band, dark strip, pale strip, tinted band, page edge, border, or split anywhere across it. Paint the environment straight through the middle of the picture as if it were any other part of the scene.
No vertical step in tone, colour, temperature, or light anywhere in the picture: the world does not change from one side of the picture to the other, and the left and right of the painting must match in brightness, colour, contrast, and finish.
No split screen, montage, comic panel, inset frame, or mirrored composition.
```

## Deterministic post-generation steps

1. Validate panel orientation and safe zones against the active dieline.
1a. (v1.1) Measure the generated base for construction bands at the four dieline x-positions — 242.5 mm, 250.5 mm, 261.5 mm, 269.5 mm — before anything is composited on it. A full-height discontinuity at any of them refuses the wrap. The prompt asks for one continuous world; this is the reading that proves it, because P0-03 shipped past eight reviews that had no way to see it.
2. Select an approved Beki pose from `cover.beki_action`.
3. Composite the exact approved pose inside the configured front Beki rectangle and record a manifest.
4. Add the Ottia title and any other cover copy as vector layers through the existing cover composer.
5. Apply the active dieline, printer boxes, color conversion, and preflight after all layers are final.

No cover anchor is invented in this contract because the active printer cover geometry is an external required input. The developer must store the approved front-panel Beki anchor in cover configuration and return it in the first-run manifest.
