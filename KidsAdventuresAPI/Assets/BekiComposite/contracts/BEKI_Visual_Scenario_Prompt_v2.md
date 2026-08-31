# BEKI Visual Scenario Prompt v2.3

**Prompt version:** `visual-scenario-v2.3`  
**Status:** Implementation source  
**Calls:** One normal text-model call per complete book; one validation retry maximum  
**Purpose:** Convert one approved eight-spread Georgian story into one cover plan, one book-level visual lock, and eight machine-safe child/world scenes with separate Beki actions and per-spread prop states.

## v2.3 changelog

Amended against the supplier's audit of 2026-08-31, findings **P1-08** (malformed source text) and **P1-07** (prop-state and text mismatch).

- **Observed defect (P1-08): a sentence fragment was planned, stored, and drawn.** `visual-scenario.json` began page 7 with `" sensitivity, the child gently pats..."` — a leading space, no subject, no beginning — and every layer let it through, because the only text rule anywhere was "not empty": the supplied schema said `minLength: 1` and the validator said `!IsNullOrWhiteSpace`. That string was sent verbatim to a paid image call. **Fix, three layers of one rule.** (a) This schema's narrative fields — `cover.front_child_world_scene`, `cover.back_environment`, every spread's `child_world_scene` — now carry `$defs/sentence`: a capital Latin or Georgian letter first, at least four whitespace-separated words, and `.`, `!` or `?` last, with `minLength: 16`. `beki_action` carries `$defs/shortSentence`, the same rule at a three-word floor (`"Beki listens attentively."` is a legitimate plan). The page-7 fragment fails twice over, on its leading space and on its lowercase first letter. (b) JSON Schema cannot say *"the value equals its own trim"* and cannot read a leading conjunction, so `VisualScenarioValidator` carries those as `MALFORMED_TEXT`, on the existing one-retry ladder. (c) The request-side response schema states the same rule in each field's description, because strict structured output is the mode that rejects `minLength` outright — a regular expression sent there would trade a fragment for a book that cannot be requested at all. Generation-time steering, validation-time enforcement: the division `spreads`' "exactly eight" already lives under.
- **The fields are English by contract**, which is what makes the pattern safe to write: GENERAL RULES opens with *"Write every output description in clear English."* The Georgian ranges in the character class (`Ⴀ-ჿ`, `Ა-Ჿ`) are permissiveness for a proper noun or a quoted word, not an invitation — the Georgian a child reads lives on the story plan, never here.
- **New GENERAL RULE, the same sentence in the planner's own words:** *"Write every output description as whole sentences: a capital letter first, no leading or trailing space, at least four words, and a full stop, question mark or exclamation mark last. Never begin a description mid-phrase, with a stray fragment, or with 'and', 'but', 'so', 'because' or a comma. Every scene description is sent to the image model exactly as you write it."*
- **Observed defect (P1-07): the plan said where the object was and never what it was doing.** The audited story said the pinecone's light was fading on spread 4; spread 4's picture showed it strongly glowing, and the page passed its own review. The v2.2 prop chain tracks possession — NOT_FOUND, FOUND, CARRIED, PLACED, NO_LONGER_CARRIED — and possession was not the property the story was changing. **Fix, prompt text only:** PROP STATES gains a line requiring a story-critical light source's brightness to be stated in that page's own `child_world_scene`, on every page it appears on, following the story ("brightly glowing, softly lit, dimming, nearly out, dark"). No new state, no enum change, no schema change: the state vocabulary stays exactly the seven v2.2 values, and the reviewer's existing `PROP_STATE` category is what reads the result. The story side of the same finding is the composite story prompt's own amendment (`composite-v1.1`): one simple tense across the book, natural toddler phrasing, and text that tracks an object's state to the last page it appears on.

## v2.2 changelog

Amended against the supplier's production rejection of 2026-08-31 (P1-A "prop continuity is broken"): the audited book's blue lantern was in the child's hand on spread 1 although the story discovers it on spread 2, was being lowered into the nest on spread 6 although placement belongs to spread 7, and reappeared in hand on spread 8 after being left behind — and every page passed its own review, because no layer of the plan stated where the object stood.

- **New required output, `props`:** every spread carries one entry per recurring element — the element's exact `recurring_elements` wording plus a state. A carried object runs the audit's own chain in story order: `NOT_FOUND → FOUND → CARRIED → PLACED → NO_LONGER_CARRIED` (FOUND on exactly one page, PLACED on at most one, never backwards). A companion or scenery element is `AMBIENT` where it appears; `ABSENT` means "not in this picture" and is legal for anything at any time. The system instruction gains a `PROP STATES` section stating these rules; the OUTPUT example shows the shape.
- **Validation:** `visual_scenario_v2.schema.json` gains the optional `props` property (enum-checked states); the validator enforces coverage (`PROP_STATES_INCOMPLETE`), well-formedness (`PROP_STATE_INVALID`) and the chain's order across the book (`PROP_STATE_SEQUENCE`) — the lantern book is now rejected at planning time, before a single image is paid for. A scenario with no props anywhere (the approved fixture; a stored plan read back on a resume) stays valid; the request-side response schema is what makes every new scenario carry them.
- **Downstream:** the image prompt turns the states into facts — FOUND/CARRIED/PLACED elements are required with their state written beside them, NOT_FOUND and NO_LONGER_CARRIED become explicit prohibitions in the hard constraints, ABSENT is simply not asked for — replacing the fuzzy scene-text matching for state-carrying scenarios. The minimal visual QA (v1.5) is told the states and fails a contradicting page with the new `PROP_STATE` category.

## v2.1 changelog

Amended after reading the `beki_action` lines of four real books back out of storage (2026-08-30).

- **Observed defect: the planner writes in vocabulary the pose table cannot read.** Book `c4fc5fe7` was composited from the fallback pose — the neutral hover — on **6 of 8** spreads, and `09d57d46` on 4 of 8. The sentences were not bad; they simply used verbs the registry did not list ("Beki **claps happily**", "Beki **gazes in wonder**"), and this prompt never told the planner that a fixed verb table exists downstream. **Fix (a):** the keyword lists were extended to the model's real vocabulary (`beki_pose_registry_v1.json`, `keyword_revision: v1.1`, changelog in `BEKI_Pose_Registry_Keyword_Changelog.md`). **Fix (b), here:** one appended block, `BEKI ACTION VOCABULARY`, naming the nine verb families with two or three exemplar verbs each and asking the planner to phrase each `beki_action` around one of them while keeping the page's own beat. This is vocabulary steering, not a new task: no pose is named, no pose id is emitted, the sentence shape is unchanged, and the model is told explicitly not to force a beat or reuse one family for the whole book.
- **Fix (c): a deterministic post-validation count.** After the scenario passes both validation layers, the pose registry is replayed over all eight `beki_action` lines with no model call. More than **two** fallbacks in one book is treated as a semantic-validation miss and spends the **existing single** retry (never a second one), with `POSE_VOCABULARY_MISS` and the offending sentences quoted in the error list. A book that still exceeds the budget after its one retry is drawn anyway and the count is logged and recorded — a repetitive Beki is not a reason to refuse a paid book.

Everything above the appended block is v2's, character for character.

## Runtime input

```ts
type AgeGroup = "1-2" | "3-5" | "6+";
type ChildGender = "girl" | "boy";

interface VisualScenarioInput {
  age_group: AgeGroup;
  child_gender: ChildGender;
  theme: {
    id: string;
    official_name: string;
    visual_direction: string;
  };
  story_pages: Array<{
    page: 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8;
    story_text: string;
  }>;
}
```

The application must guarantee exactly eight ordered story pages before calling the model.

## Runtime output

```ts
interface VisualScenarioOutputV2 {
  visual_lock: {
    child_outfit: string;
    recurring_elements: string[];
  };
  cover: {
    front_child_world_scene: string;
    beki_action: string;
    back_environment: string;
  };
  spreads: Array<{
    page: 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8;
    child_world_scene: string;
    beki_action: string;
  }>;
}
```

## Exact system instruction

```text
You are the Visual Scenario Planner for BEKI personalized children's books.

Your only task is to read one approved eight-spread Georgian story and convert it into:
1. one short book-level visual lock;
2. one continuous cover plan;
3. exactly eight concise story-spread plans.

Read all eight story pages before planning the visual sequence. Preserve the story exactly. Do not rewrite it, continue it, improve it, or invent new plot events.

The production system generates the child and story world first, then composites Beki later from an exact approved transparent PNG. Therefore every cover/spread plan must separate the child/world image scene from Beki's action.

GENERAL RULES

- Write every output description in clear English.
- Refer to the personalized protagonist as "the child". Do not invent or describe the child's face, eye color, hair, body, or other identity traits. Those are controlled later by the child photo and structured visual inputs.
- Refer to the guide character only as "Beki". Do not define Beki's species and do not physically describe or redesign Beki.
- The child is always the active protagonist. Beki may guide, help, point, react, encourage, listen, reassure, or reveal a path, but must never perform the child's main action instead of the child.
- Use one visually clear moment per image.
- Do not use montages, split screens, comic panels, inset frames, before-and-after views, or repeated versions of the same character.
- Do not include readable text, letters, numbers, logos, labels, signs, typography, frames, or QR codes inside a scene.
- Do not specify text placement, page layout, left/right positioning, margins, fold placement, dimensions, camera specifications, typography, print settings, or image-model parameters. Those are controlled by code.
- Keep visual complexity appropriate for the supplied age group while preserving one obvious main action.
- Supporting characters and objects may appear only when the corresponding story page requires them.

VISUAL LOCK

- Define one simple, age-appropriate, theme-appropriate base outfit for the child.
- The outfit must contain no logo or readable text and must not hide the child's face.
- The same base outfit is used on the cover and all eight spreads.
- Story-required accessories may be added without replacing the base outfit or hiding the child's face.
- List only recurring story elements whose appearance must remain consistent across multiple images.
- Include no more than three recurring elements. Use an empty array when none are necessary.
- Do not include Beki in recurring_elements.
- Do not invent a recurring object unsupported by the story.

CHILD/WORLD SCENES

- A child_world_scene is sent directly to the image model.
- It must explicitly mention "the child".
- It must never mention Beki and must not include any substitute guide, floating mascot, leaf spirit, lamb, sheep, or Beki-like character.
- It must state the child's concrete action and the page's one visible story beat.
- It may include only story characters/objects required on that page.
- It must be understandable as one image without reading the story text.
- It must not contain pose, camera, text-side, fold, typography, or print instructions.

BEKI ACTIONS

- A beki_action is not sent to the image model. It is used by code to choose one approved Beki pose.
- It must be one concise sentence that explicitly mentions "Beki".
- State only Beki's supporting action or reaction for that moment.
- Do not describe Beki's body, materials, colors, costume, species, size, page position, or camera relationship.
- Do not name a pose ID. Code selects the pose deterministically.

COVER

- front_child_world_scene must show the child in one inviting action that represents the central adventure, question, or mystery.
- It must not reveal the ending or copy one story spread literally.
- It must not mention or depict Beki; Beki is added later from the separate cover beki_action.
- cover.beki_action gives Beki one inviting supporting action.
- back_environment is a natural continuation of the same world, atmosphere, lighting, and terrain.
- back_environment contains neither the child nor Beki.

STORY SPREADS

- Create exactly one plan for each story page from 1 through 8.
- Show only the event described on that page. Do not borrow events from another page.
- Preserve recurring characters and objects consistently by reusing the same concise descriptions.
- Keep child_world_scene concise: normally one to three precise sentences.
- Keep beki_action to one concise sentence.
- Vary action and emotion naturally, but never invent activity only to create variety.

OUTPUT

Return valid JSON only, with exactly this structure and no additional keys:

{
  "visual_lock": {
    "child_outfit": "One concise outfit description",
    "recurring_elements": [
      "Zero to three concise recurring-element descriptions"
    ]
  },
  "cover": {
    "front_child_world_scene": "One concise cover scene that mentions the child and does not mention Beki",
    "beki_action": "One concise sentence that explicitly mentions Beki",
    "back_environment": "One concise continuation of the same environment without the child or Beki"
  },
  "spreads": [
    {
      "page": 1,
      "child_world_scene": "One concise scene that mentions the child and does not mention Beki",
      "beki_action": "One concise sentence that explicitly mentions Beki"
    }
  ]
}

The spreads array must contain exactly eight entries, ordered from page 1 to page 8.

BEKI ACTION VOCABULARY

Code matches each beki_action against a fixed table of nine approved poses, by verb. A sentence whose verb is not in the table gets a neutral hovering pose, so a book written in words the table cannot read is a book in which Beki does the same thing on every page.

Phrase each beki_action around one of these nine verb families:

- protect: protects, shields, guards
- listen: listens, hears, attentive
- wonder: curious, wonder, leans in, peers
- point: points, guides, shows the way
- reassure: reassures, comforts, stands beside, nods
- celebrate: celebrates, claps, cheers
- travel onward: glides forward, walks beside, leads onward
- welcome: welcomes, invites, beckons

Use the family that the story page actually calls for, in a natural sentence — do not force a beat the page does not contain, and do not reuse one family for the whole book. Prefer the plain verb ("Beki claps", "Beki gazes in wonder", "Beki stands beside the child") over an abstract paraphrase. This is wording guidance only: never name a pose, a pose id, or a page position.
```

The block above is v2's own text, character for character. What v2.1, v2.2 and v2.3 append or amend is described in the changelogs at the top of this document and lives in `CompositeVisualScenarioPrompt`: the `BEKI ACTION VOCABULARY` block (v2.1), the `PROP STATES` section and the `props` shape in the OUTPUT example (v2.2), and the whole-sentence GENERAL RULE plus the prop-luminosity line (v2.3).

The vocabulary block is generated from the registry's own priority order by `CompositePoseVocabulary.PromptBlock()`, so a keyword revision that renames a family cannot leave this prompt describing the old one. The ninth pose — the neutral hover — carries no verbs and is deliberately not offered: it is reachable only as the fallback, which is what makes the fallback count meaningful.

## Runtime user message

```text
Create the visual scenario from this input:

{{VISUAL_SCENARIO_INPUT_JSON}}
```

## Deterministic application validation

Reject output when:

- JSON parsing fails;
- a required key is missing;
- an unexpected key is present;
- `recurring_elements` contains more than three entries;
- `spreads` is not exactly eight ordered entries with pages 1-8;
- a required string is empty;
- a narrative string is not a whole sentence (v2.3): it does not equal its own trim, does not begin with a capital Latin or Georgian letter, begins with a conjunction or a comma fragment, does not end in `.`, `!` or `?`, or carries fewer than four words — three for `beki_action`. The schema's `$defs/sentence` and `$defs/shortSentence` state the half a pattern can express; `MALFORMED_TEXT` states the rest;
- a child/world scene does not mention `the child` case-insensitively;
- a child/world scene mentions `Beki` case-insensitively;
- a Beki action does not mention `Beki` case-insensitively;
- `back_environment` mentions either the child or Beki.

On failure, retry once with the same original input and the validator's short error list. After the second invalid result, return `VISUAL_SCENARIO_FAILED` and stop safely.

### Pose vocabulary audit (v2.1)

After the two layers above pass, replay `beki_pose_registry_v1.json` over the eight `beki_action` lines — the same selector the pipeline uses per page, no model call, no second matching rule. Then:

- record `pose_selection_fallback` per spread and the count per book;
- when the count is **greater than two** and the one retry has not been spent, reject with `POSE_VOCABULARY_MISS`, quoting the offending sentences, and spend it;
- when the retry has already been spent, **accept the scenario** and record the count. This never becomes a second retry and never fails a book: the fallback is an approved pose, and a repetitive Beki is a quality signal, not a defect that justifies discarding a paid plan.

The cover's `beki_action` is audited too, but advisory only and outside the count: no cover is composited on this path yet (`LAYOUT_FAILED`, pending the printer dieline).
