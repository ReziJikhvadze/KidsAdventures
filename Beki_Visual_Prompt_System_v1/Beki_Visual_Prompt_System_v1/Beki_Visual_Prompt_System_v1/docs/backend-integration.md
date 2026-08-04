# Backend Integration — Beki Visual Prompt System v1

## Goal

Integrate the new Beki visual pipeline so that illustration generation becomes deterministic, auditable, and brand-consistent.

## Recommended services

```text
/services/visual/
  photo-quality-check.ts
  analyze-character-identity.ts
  build-visual-bible.ts
  generate-hero-anchor.ts
  build-page-image-prompt.ts
  build-cover-image-prompt.ts
  generate-image.ts
  review-image.ts
  repair-image.ts
  visual-pipeline.ts
```

## Sources of truth

- Story JSON approved by Story Pipeline
- Official Beki reference asset stored in backend
- Child photo(s) uploaded by parent
- Visual prompt files from `prompts/`
- Schemas from `schemas/`

## Recommended flow

### 1. Story approval gate
Only proceed to visual generation after the story pipeline has produced approved story JSON.

### 2. Photo quality gate
Validate that the child portrait is suitable. Reject and request re-upload if the face is too small, heavily occluded, blurry, or unusable.

### 3. Character Identity Analysis
Call the model with `character-identity-analyzer-v1.md`.
Store the resulting JSON.

### 4. Build Visual Bible
Call the model with `visual-bible-builder-v1.md` using:
- approved story JSON
- Character Identity Spec
- child profile
- layout config
- Beki canonical description
- approved extra-wish mode

Store the Visual Bible JSON.

### 5. Generate Hero Character Anchor
Use the child photo + Visual Bible with `hero-character-anchor-v1.md`.
This creates the stylized master character image for the child.

### 6. Build Cover/Page prompt(s)
For each asset:
- Cover -> `cover-image-generator-v1.md`
- Interior page -> `page-image-generator-v1.md`

Each page must receive:
- child photo reference
- hero anchor reference
- official Beki reference if Beki is present
- guest references if relevant
- page scene spec
- continuity state

### 7. Generate image
Use `gpt-image-2` (recommended current production model) through the Images API.
- create the cover first
- create pages sequentially or in small controlled batches after references are established

### 8. Review image
For each generated image call `visual-reviewer-v1.md`.
If decision is:
- `approve` -> continue
- `repair` -> run targeted repair
- `regenerate` -> regenerate the page with strengthened prompt

### 9. Repair image (if needed)
Use `visual-repair-v1.md` and the existing generated image as edit input.

### 10. Programmatic layout
Only after image approval, place the following outside the image model:
- Georgian story text
- page number
- book title
- CTA (page 12)
- QR code

## Important implementation notes

### Beki asset handling
Store the official Beki image as a canonical backend asset and attach it only on pages where `charactersPresent` includes `Beki`.

### Never do this anymore
- do not truncate story text at 600 chars
- do not use page 1 alone as the hero master reference
- do not build the prompt as one flat paragraph
- do not pass raw parent free text directly into image prompts
- do not generate text or QR codes in the illustration
- do not put `adventureId` or `page title` inside the image prompt

### Recommended prompt assembly format
Use short labeled sections rather than a flat paragraph. Easier to debug, inspect, and version.

### Portrait default
Interior illustrations should default to portrait single-page assets. Two-page spreads can be composed later in layout if needed.

### Logging and versioning
Store with every asset:
- prompt version
- model version
- source scene id
- adventure id
- review decision
- repair history

## Minimal orchestration outline

```text
approved story
  -> validate visual input
  -> analyze identity
  -> build visual bible
  -> generate hero anchor
  -> generate cover
  -> review cover
  -> repair/regenerate if needed
  -> generate page 1..12
  -> review each page
  -> repair/regenerate page as needed
  -> place text/CTA/QR
  -> export final book
```
