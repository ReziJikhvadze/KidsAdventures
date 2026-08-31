# BEKI Story Boundary v1.1

**Contract version:** `story-boundary-v1.1`  
**Status:** Locked MVP boundary, not a replacement creative prompt

The exact approved Story prompt and provider-specific response schema must be taken from the active backend branch. The archived `MasterStoryPromptV6.md` must not be copied into the new pipeline because it contains superseded requirements.

## v1.1 changelog

Amended against the supplier's audit of 2026-08-31, finding **P1-07 — "Story continuity and scene-to-text mismatches need correction"**. The input and output boundaries are untouched, to the field: v1.1 adds three locked rules about the Georgian copy itself, and names the prompt version that implements them — `composite-v1.1`, in `MasterStoryPromptComposite`.

- **Observed defect: the book alternated tenses.** The audited story carried present forms (`ქრება`, `ანათებს`) beside aorists (`გამოვიდა`, `გაჰყვნენ`, `აინთო`). Read aloud to a two-year-old, one book becomes two storytellers. **Locked:** one simple tense across all eight spreads, chosen on spread 1 and held; the present tense is the natural choice for this age band.
- **Observed defect: unnatural toddler phrasing.** The book said `მას ძილი ნებავს`. **Locked:** everyday spoken Georgian, never a bookish or archaic construction. The audit's own canonical pair is the example the prompt now carries — prefer `მას ეძინება` over `მას ძილი ნებავს` — **pending Georgian editor approval**, which is the audit's own wording and the reason the deterministic checklist rule beside it (`unnatural_toddler_phrasing`, `georgian-text-checklist-v1.1`) flags for a human and never rewrites.
- **Observed defect: the text contradicted the pictures about an object.** Spread 4's story said the pinecone's light was fading while spread 4's illustration showed it strongly glowing, and spread 8 dropped the object the ending depended on. The story text is what the visual scenario and the image model are given, so a story that contradicts itself about an object contradicts every picture drawn from it. **Locked:** the copy tracks each important object's state — who has it, where it is, and how brightly it is shining if it gives off light — from the page that introduces it to the last page it appears on. A prop the story has said is fading is never described again as shining unless the story lights it again.

The visual half of the same finding is the Visual Scenario contract's `v2.3` amendment (prop luminosity stated per page) and the existing `PROP_STATE` review category. The Georgian editorial pass itself remains a human step; this contract fixes what the prompt asks for, not what a person still has to read.

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
- (v1.1) One simple tense across all eight spreads; the present tense unless the whole book is written otherwise.
- (v1.1) Natural spoken Georgian for the age band — `მას ეძინება`, not `მას ძილი ნებავს` (pending Georgian editor approval).
- (v1.1) The copy tracks an important object's state, luminosity included, from the page that introduces it to the last page it appears on.
