# Visual QA Test Plan — Beki Visual Prompt System v1

Run these tests before launch.

## 1. Child identity consistency
- same child recognizable across cover + 12 pages
- no age drift
- no hair color drift
- no facial redesign after page 1

## 2. Beki consistency
- Beki appears only on intended pages
- Beki keeps exact colors, ears, proportions, eyes, and texture
- Beki remains visually secondary to the child

## 3. Outfit continuity
- child story outfit remains the same across the full book unless explicitly changed
- recurring accessories stay consistent

## 4. Supporting cast continuity
- recurring companions keep the same look/clothes/scale
- no duplicate companions appear accidentally

## 5. Scene accuracy
- each page illustration matches the approved page scene spec
- the child is actively doing the page action

## 6. Composition
- text-safe area is actually usable
- hero remains focal point
- no important subject placed where later text will cover it

## 7. No-text rule
- no letters, numbers, page text, labels, or QR-like shapes appear in the art
- no logos or watermarks

## 8. Print suitability
- image looks good in portrait book layout
- enough clean space for overlay text
- no critical subject cropped awkwardly

## 9. Story continuity
- time of day and location progress logically page to page
- object states remain consistent (open door stays open, held item remains held, etc.)

## 10. Safety and tone
- no frightening or distressing imagery
- expressions remain emotionally safe and age-appropriate

## 11. Hand/face quality
- no major anatomy issues
- eyes and hands readable
- face not distorted

## 12. Repair loop reliability
- verify that local repair preserves everything else
- verify that Beki-specific repair does not redesign the child

## 13. Low-quality photo rejection
- if the child photo is poor, system must reject it rather than silently produce a generic child

## 14. Third-party character handling
- in `originalize` mode, output must not resemble a direct copy of a copyrighted character
- in `exclude` mode, the character must not appear

## 15. Layout overlay test
- place final Georgian text over approved art and confirm readability
- place CTA and QR on page 12 and confirm no collision with focal content

## 16. Regression test set
Maintain a fixed internal test set with at least:
- one 2–4 magical world book
- one 5–7 adventure book
- one 8–10 mystery book
- one story with Beki on only 3 pages
- one story with recurring guest characters
