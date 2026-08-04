# Beki Visual Prompt System v1

This package contains the production-ready visual prompt system for Beki personalized storybooks.

## Package structure

- `prompts/` — system prompts for the visual pipeline
- `schemas/` — JSON schemas for input/output and intermediate structured artifacts
- `examples/` — sample input payloads
- `docs/` — backend integration and QA instructions

## Visual pipeline overview

1. Photo Quality Check
2. Character Identity Analyzer
3. Visual Bible Builder
4. Hero Character Anchor generation
5. Cover/Page Scene Spec generation (from approved story JSON)
6. Cover/Page Image Generation
7. Visual Review (QA)
8. Visual Repair (only if needed)
9. Programmatic text/CTA/QR placement
10. Final export

## Required references

- Child portrait photo(s)
- Official Beki reference image (canonical asset stored in backend)
- Optional guest character references, if approved

## Important product rules

- The child is always the visual hero.
- Beki is a recurring guide and friend, visually secondary to the child.
- No text, numbers, labels, logos, or QR codes should be drawn by the image model.
- The real child photo is used for identity only — not for copying clothing, background, or pose.
- Use a single approved story outfit across the cover and all interior pages unless a deliberate costume change is explicitly requested by the story.
- Use portrait single-page illustrations for interior pages by default.
