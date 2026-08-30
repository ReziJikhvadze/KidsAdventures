# BEKI Visual Scenario Prompt v2.1

**Prompt version:** `visual-scenario-v2.1`  
**Status:** Implementation source  
**Calls:** One normal text-model call per complete book; one validation retry maximum  
**Purpose:** Convert one approved eight-spread Georgian story into one cover plan, one book-level visual lock, and eight machine-safe child/world scenes with separate Beki actions.

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
