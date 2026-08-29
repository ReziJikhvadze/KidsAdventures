# BEKI Story Boundary v1

**Contract version:** `story-boundary-v1`  
**Status:** Locked MVP boundary, not a replacement creative prompt

The exact approved Story prompt and provider-specific response schema must be taken from the active backend branch. The archived `MasterStoryPromptV6.md` must not be copied into the new pipeline because it contains superseded requirements.

## Input boundary

Story receives only:

```json
{
  "child_name": "string",
  "child_age": 1,
  "child_gender": "girl or boy",
  "theme_id": "canonical mapped theme ID"
}
```

The child photo, appearance fields, Visual Scenario instructions, image composition, typography, print settings, and legacy Extra Wish are forbidden at this boundary.

## Required normalized output

```json
{
  "title_ka": "Georgian title",
  "story_pages": [
    {"page": 1, "story_text": "Georgian story copy"},
    {"page": 2, "story_text": "Georgian story copy"},
    {"page": 3, "story_text": "Georgian story copy"},
    {"page": 4, "story_text": "Georgian story copy"},
    {"page": 5, "story_text": "Georgian story copy"},
    {"page": 6, "story_text": "Georgian story copy"},
    {"page": 7, "story_text": "Georgian story copy"},
    {"page": 8, "story_text": "Georgian story copy"}
  ]
}
```

Validate with `story_boundary_v1.schema.json`. Provider-specific fields may exist upstream, but they must be mapped once into this boundary and must not leak into downstream task contracts.

## Locked behavior

- Output story copy is Georgian only.
- Output contains exactly eight ordered story spreads.
- The child is the active protagonist.
- Beki guides, reacts, encourages, or reveals a path; Beki does not solve the child's main problem.
- The story does not need to include English translations or image prompts.
- The application maps numeric age to `1-2`, `3-5`, or `6+` once for downstream use.
- Unknown gender or theme values are rejected rather than guessed.
