# BEKI Minimal Visual QA v1.5

**Prompt version:** `minimal-visual-qa-v1.5`  
**Status:** Implementation source  
**Purpose:** Review only parent-visible critical failures after exact Beki compositing.

## v1.5 changelog

Amended against the supplier's production rejection of 2026-08-31 (P1-A prop continuity, P1-B shot rhythm). v1.3 made the shot judgement advisory pending evidence; the evidence arrived as a rejected book, so the clear cases become checks while the borderline impression keeps its note.

- **New category `PROP_STATE`:** the page description now quotes the plan's own object states (Visual Scenario v2.2's `props`), and a picture that contradicts one — the object visible before its discovery or after being left behind, or missing while the plan says the child is discovering, holding, or placing it — fails with `recommended_action: regenerate_base`. The audited lantern passed eight reviews because no reviewer was ever told where the lantern was supposed to be.
- **New category `SHOT_COMPLIANCE`:** a rendering that clearly contradicts the stated shot — wrong camera distance, a required full figure not fully visible, the main story subject cropped by the canvas edge — is a failure with `recommended_action: regenerate_base`, bounded by the unchanged one-regeneration budget. `shot_note` survives for borderline impressions only and still changes nothing on its own.
- **Schema:** the two names join the `failed_checks` enum in `minimal_visual_qa_v1.schema.json` — additive, so every answer valid under v1.4 remains valid.

## v1.4 changelog

Amended on the owner's product decision of 2026-08-30, after pack `7fc8faf4` died on it:

> "we must agree on entered age, name, eye color etc — but the image is the reference. It might be an older image and [the parent] wants the book for the child's younger age, so it must not be a blocker."

- **Observed defect: a book lost to CHILD_AGE, twice.** `7fc8faf4`'s spread 1 came back `FAIL (regenerate_base): CHILD_AGE`, bought its one regeneration, came back `FAIL (regenerate_base): CHILD_AGE` again, and the pack stopped. Both verdicts are in that pack's own `spread-01-qa.json`. Nothing was wrong with the pictures: a parent may upload a photograph taken a year ago and buy the book for the age they entered, and a reviewer asked to compare the render against that photograph will call the difference a fault every single time. **Fix:** `CHILD_AGE` is no longer a failed-check category. It is removed from the schema's `failed_checks` enum and from the numbered list below, and the reviewer is told the precedence rule instead: the photograph says WHO the child is; the entered age, name and eye colour say how old the child is here and what colours to draw.
- **Advisory instead, on the `shot_note` model.** `minimal_visual_qa_v1.schema.json` gains **one optional** string property, `age_note`. It is not a failed check, cannot appear in `failed_checks`, cannot change `status` or `recommended_action`, and can never cause a regeneration, a re-composite or a retry. A page whose only remark is an age note is a `PASS`. It is recorded on the spread's review record and on the book's review document as an `age_advisories` entry, beside the entered age, so the decision can be revisited against counted evidence rather than one refused pack.
- **A reviewer that names it anyway is understood, not argued with.** `CHILD_AGE` is stripped from `failed_checks` **before** the answer is validated, and a `FAIL` whose only objection it was becomes the `PASS` it should have been. Validating first would reject the answer against the new enum, spend the parse retry, and turn an advisory into a harder blocker than the one that was removed.
- **CHILD_IDENTITY is untouched and stays fully blocking.** Likeness and the eight locked attributes — face shape, hair, eyebrows, eye colour, skin tone, glasses, distinctive features, outfit — are still the contract, and a book still stops when they are wrong. What came off is the *age*, which the parent, not the photograph, decides.

## v1.3 changelog

Amended alongside the image template's v1.3, after the supplier's PDF audit reported that the wide, action and close spreads all render as similar medium compositions.

- **Observed defect: nothing measures whether the deterministic shot was obeyed.** The shot instruction is injected per page from `pipeline_config_v1.json`, and no stage — deterministic or model — ever looked at whether the picture that came back was that shot. The only evidence was a human flicking through a printed proof. **Fix:** the page description now states the shot this spread was asked for, and the reviewer may return one optional free-text `shot_note` when the rendered composition **clearly** contradicts it.
- **Advisory only, and deliberately toothless.** `shot_note` is **not** a failed-check category, cannot appear in `failed_checks`, cannot change `status`, cannot change `recommended_action`, and cannot cause a regeneration, a re-composite or a retry. A page whose only remark is a shot note is a `PASS`. The reviewer is told this in the instruction, because a reviewer that believes a note has teeth writes it defensively. The reason for no gate is the supplier's own: shot-type judgement from a single frame is subjective, the false-positive cost is a paid image call, and there is not yet evidence to price it. This note is how that evidence gets collected.
- `minimal_visual_qa_v1.schema.json` gains **one optional** string property, `shot_note`. Every answer that was valid under v1.2 is still valid — the property is not in `required`, and the shape's `additionalProperties: false` is what made adding it a schema change rather than a free-form note.

Nothing else moved. The nine category names, `status`, `failed_checks`, `recommended_action`, `notes`, the deterministic validation list and the retry ladder are v1.2's.

## v1.2 changelog

Amended alongside the image template's v1.2, after the owner's review of the first v1.1 books: *"eye color gets fucked up almost always especially on the cover"*.

- **Observed defect: the reviewer had nothing to check against.** It was shown the composite, the photograph and, from v1.1, the anchor — and asked whether the child looked like the same child. That is an impression, and an impression cannot fail an eye colour. **Fix:** the book's identity spec is written into the ask, and `CHILD_IDENTITY` now covers eyebrows, face shape, glasses (present when the spec says none, absent when it describes them, or a materially different style) and outfit details against the anchor. The eye colour is checked **by name**: *"The child's eyes must read as {eye_colour} in this illustration. If they do not, that alone is a CHILD_IDENTITY failure."*
- **Observed defect: the cover was never reviewed at all.** The cover a parent judges the book by was the one the preview drew, adopted into the finished pack without a second look — no identity spec, no anchor, and on a composite plan not even an eye colour in its prompt. It is the picture the owner watched fail most often, and the only one nothing checked. **Fix:** § *Cover review*.

Nothing else moved. The nine category names, the response shape, `minimal_visual_qa_v1.schema.json`, the deterministic validation list and the retry ladder are v1.1's.

## Cover review

The cover is redrawn once after spread 1 is accepted — by the legacy upright-cover composition, with the identity lock written into it and the accepted first spread attached as the appearance anchor — and reviewed by this same reviewer, with this same system instruction and schema, against a cover-shaped page description:

- it is stated plainly that this is the cover, that it carries no printed story text and has no central exclusion zone, and that `TEXT_SAFE_AREA` and `FOLD_SAFETY` therefore do not apply to it;
- the identity spec, the eye colour by name and the glasses rule are stated exactly as they are for a spread;
- the anchor is attached, so "the same child as the rest of the book" is a comparison rather than an intuition.

One regeneration. A cover refused twice is **not** a book failure: the previewed cover the parent already saw is kept, the fulfilment manifest records which of the two provenances shipped, and the book completes. A book must not die for its cover.

## v1.1 changelog

Amended under the handoff's own change rule — *only observed defects become the next implementation rules* — after the first real books were run on 2026-08-29.

- **Observed defect: child identity drift on an all-pass book.** A completed composite book in which every spread returned `PASS` showed a visibly different child from page to page and lost the eye colour. This reviewer could not have caught it: it was shown the composite and the child's photograph and nothing else, so it judged each page against the photograph independently — which is exactly what a drifting book does too — and `CHILD_IDENTITY` named no attribute a reviewer could compare. **Fix:** on every spread after the first, the reviewer is additionally shown the **child appearance anchor** (the accepted spread-1 base), and `CHILD_IDENTITY` now names the four attributes to compare against it. Page-to-page drift becomes a comparison rather than an intuition.
- **Observed defect: the `recomposite_beki` retry was a no-op.** A book failed at spread 7 with `IMAGE_QA_FAILED (spread 7): FAIL (recomposite_beki): FOLD_SAFETY`, after spreads 1-6 had passed. The retry that was supposed to save it re-composited the same base with the same pose at the same deterministic anchor — arithmetic that is deterministic by design — so the second image was byte-for-byte the first image, and the reviewer refused it again in the same words. The pack paid for two reviews of one picture and stopped. §14 had already said what to do instead: *"A failed placement should first adjust deterministic anchors, not redraw Beki."* **Fix:** the one placement retry now *moves* her (§ *Retry ladder*), and a placement the reviewer refuses twice escalates to the base-image budget it has not yet spent.
- **Observed defect: a page marked for human review left nothing to review.** The same pack's directory held spreads 1-6 and no trace of spread 7 — the composite that was generated, paid for and judged was discarded with the exception. **Fix:** a terminal `IMAGE_QA_FAILED` now carries the refused composite and an attempt record out to the fulfilment layer, which stores them beside the book's other artifacts as `spread-NN-failed.png` and `spread-NN-qa.json`.

Nothing else moved. The nine category names, the response shape, `minimal_visual_qa_v1.schema.json` and the deterministic validation list are v1's. The parse retry is still ×1. What changed is what a refused page *does* next, below.

## Retry ladder

A page gets at most two generated base images and at most three reviews, in this order and no other:

1. **`regenerate_base`, once.** The reviewer says the fault is in the child/world image, so a second one is bought. Beki returns to the approved anchor for it.
2. **`recomposite_beki`, once, and it must change the placement.** Beki's visible centre moves away from the centre of the sheet by 0.06 of the canvas width, towards the half of the spread she already occupies, and her visible height is drawn at 0.9 of its configured value. Both are clamped so the visible sprite stays fully inside the canvas and clear of the reserved text third. No new image is generated: the same base is re-composited at the adjusted anchor, which is recorded in the composition manifest in the ordinary fields. Mirroring, rotation, warping, recolouring and AI redraw remain impossible. If the clamp leaves nowhere to move her, the rung is skipped rather than spent on an identical picture.
3. **The unused base budget, if step 2 did not fix it.** A placement refused twice is evidence about the picture rather than about the placement, so if the one regeneration has not been spent it is spent now, at the approved anchor. A *first* verdict of `human_review` still stops the book: "the failure source is ambiguous" is not a thing another picture answers.

Then `IMAGE_QA_FAILED`, with the refused composite and the attempt record persisted for a person.

## Inputs to the multimodal reviewer

1. Original consented child photo: identity and approximate age reference only.
2. Final child/world plus exact-Beki composite.
3. Current `child_world_scene`.
4. Current `beki_action`.
5. Book-level `child_outfit` and relevant recurring elements.
6. Deterministic `text_side` and central exclusion-zone description.
7. The book's child identity spec, including the eye colour and the glasses field, written into the page description.
8. On every spread after the first: the child appearance anchor — the final QA-accepted spread-1 base image, the same picture the image model was shown. Absent on spread 1, which is the page that produces it.

Approved asset hashes, mirroring/rotation flags, alpha bounds, file readability, and dimensions are checked by code before this call. Do not ask the reviewer to infer cryptographic identity or file metadata from pixels.

## Exact system instruction

```text
You are the Minimal Visual QA reviewer for BEKI personalized children's books.

Review only critical, parent-visible failures. Do not score beauty, creativity, minor stylistic variation, tiny background artifacts, or subjective preferences. Do not request a retry merely to improve an already usable image.

Use the original child photo only to judge whether the illustrated child remains recognizably the same child. Do not require photorealism.

The photograph says WHO the child is. It does not say how old the child is in this book: the age, the name and the eye colour are the parent's entered values, and the book is drawn to those. A photograph may have been taken a year or two ago, and a parent may deliberately be buying the book for a younger age. Never fail an illustration because the child looks older or younger than the photograph, or than the stated age.

When a child appearance anchor is supplied, use it only to judge whether this page's child is the same stylized child as the rest of the book. It is not a composition, pose, or background reference.

Check exactly these categories:

1. CHILD_IDENTITY - The illustrated child is not recognizably the supplied child; or the child's eyes do not read as the stated eye colour; or the child has materially different hair colour/style, eyebrows, face shape, skin tone, or outfit details from the child appearance anchor; or glasses are present when the spec says none, absent when the spec describes them, or a materially different style of frames.
2. OUTFIT_CONTINUITY - The required base outfit is missing or materially changed.
3. MAIN_SCENE_BEAT - The one required visible story event is missing, contradicted, or replaced by a different event.
4. CAST_ERROR - The child or a required supporting character is missing, duplicated, or replaced; or an unrequested prominent character appears.
5. GENERATED_TEXT - Readable text, pseudo-text, logo, label, sign, watermark, or QR appears in the illustration.
6. TEXT_SAFE_AREA - A face, hand, character, foreground object, or key action blocks the reserved text side.
7. FOLD_SAFETY - A face, hand, character, or story-critical detail crosses or touches the central exclusion zone.
8. BEKI_INTEGRATION - Beki is duplicated, clipped, hidden, materially obstructs the main action, or is visibly pasted into an unsuitable hard-edged/foreground area.

Do not fail Beki for artistic anatomy or exact asset identity; those are enforced by the approved PNG hash. Do not fail for small differences in background detail. Do not rewrite the prompt.

Return valid JSON only. Use PASS when no critical category fails. Use FAIL when at least one critical category fails.

Choose one recommended_action:
- pass: no critical failure;
- regenerate_base: failure originates in the child/world generation;
- recomposite_beki: base image is usable and only deterministic Beki placement is wrong;
- human_review: the failure source is ambiguous or a second attempt has already failed.

Return exactly this structure and no additional keys:

{
  "status": "PASS",
  "failed_checks": [],
  "recommended_action": "pass",
  "notes": [],
  "shot_note": "",
  "age_note": ""
}

Each failed_checks item, when present, must be one of:
CHILD_IDENTITY, OUTFIT_CONTINUITY, MAIN_SCENE_BEAT, CAST_ERROR, GENERATED_TEXT, TEXT_SAFE_AREA, FOLD_SAFETY, BEKI_INTEGRATION.

age_note is optional, advisory, and never a failure. Fill it with one short sentence only when the illustrated child reads as a clearly different age from the stated one. Leave it out, or empty, otherwise. An age_note is not a failed check, does not appear in failed_checks, does not change status or recommended_action, and never causes a retry. The age the parent entered is the age the book is drawn to, whatever the photograph shows.

shot_note is optional, advisory, and never a failure. The page description states the shot this spread was asked for. Fill shot_note with one short sentence only when the rendered composition clearly contradicts that shot type - for example a close-up where a wide establishing view was asked for. Leave it out, or empty, when the shot is right or you are unsure. A shot_note is not a failed check, does not appear in failed_checks, does not change status or recommended_action, and never causes a retry.

Keep notes short, concrete, and visible in the supplied composite. Do not include sensitive descriptions of the child's source photo.
```

The category name `FOLD_SAFETY` is unchanged and stays unchanged: it is a value in `minimal_visual_qa_v1.schema.json`'s enum, and renaming it would make every stored verdict and every reviewer answer invalid against the supplied schema. What it names is the central exclusion zone.

## Deterministic output validation

- `status` is exactly `PASS` or `FAIL`;
- `failed_checks` contains only allowed unique values;
- `PASS` requires an empty `failed_checks` array and `recommended_action = pass`;
- `FAIL` requires at least one failed check and a non-`pass` action;
- `recommended_action` is one of `pass`, `regenerate_base`, `recomposite_beki`, `human_review`;
- `notes` is an array of short strings;
- `shot_note`, when present, is a string; it is advisory and is never read as a failure;
- unexpected keys are rejected.

Retry JSON parsing once without rerunning image generation. A second invalid QA response returns `IMAGE_QA_FAILED` for human review.

A verdict that fails takes the page up the retry ladder above. A page that reaches the end of it stops with `IMAGE_QA_FAILED`, and the composite the reviewer refused is stored with the attempt record rather than discarded.
