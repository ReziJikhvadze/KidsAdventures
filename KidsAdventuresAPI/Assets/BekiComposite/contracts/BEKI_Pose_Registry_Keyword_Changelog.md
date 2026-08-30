# BEKI pose registry — keyword changelog

The registry JSON carries no comments, so every keyword amendment is recorded here: what changed,
which observed defect it answers, and which collisions the wording deliberately avoids.

`registry_version` names the **pack revision** — the nine approved PNGs, their SHA-256 hashes,
`priority_order`, `fallback_pose_id` and `forced_usage`. None of those move in a keyword amendment,
and `pipeline_config_v1.json` pins the string (`BekiCompositeEngine` refuses to start when the two
disagree), so a keyword change carries its own field: `keyword_revision`.

| keyword_revision | registry_version | date | reason |
| --- | --- | --- | --- |
| v1.0 (implicit) | beki-pose-registry-v1 | supplier pack | as delivered |
| v1.1 | beki-pose-registry-v1 (unchanged) | 2026-08-30 | R13a — pose fallback was rampant on real books |

## v1.1 — the scenario model's actual vocabulary

### Observed defect

Two completed books, read back from their stored `visual-scenario.json`:

- `c4fc5fe7` composited `pose_01_neutral_hover` — the **fallback** — on 6 of 8 spreads. Every one
  of its Beki sentences was a perfectly ordinary supporting action; none of them contained a v1.0
  keyword.
- `09d57d46` fell back on 4 of 8. Two are worth quoting because they name the whole fault:
  - "Beki **claps happily** at the discovery of the glowing stone." — the celebrate pose listed
    `celebrates`, `cheers`, `applauds`; it did not list the one verb a model actually reaches for.
  - "Beki **gazes in wonder** at the grand sight." — the curious pose listed `wonders` (verb) and
    the sentence says `wonder` (noun).

A fallback is not a neutral outcome: it is the neutral hover, the same drawing on every page, and
the reader sees a book whose guide never reacts to anything.

### The fix, and what it deliberately is not

Only the `keywords` arrays grew, and only by **appending**. Nothing was removed, reordered or
reworded, which keeps two things true: every sentence that matched under v1.0 still matches the same
pose, and the *recorded* `matched_keyword` for those sentences is unchanged (the selector reports the
first keyword that hits in list order, and the v1.0 entries still come first).

The matching strategy is untouched — evaluate poses in `priority_order`, first case-insensitive
substring wins, otherwise `fallback_pose_id`. No word boundaries, no stemming, no scoring. A book
composited months from now from the same scenario still comes out identical, and an operator can
still predict the pose by reading the file.

Candidates were derived from the four stored scenarios' 45 real `beki_action` lines plus close
synonyms; all 45 now resolve, none to the fallback.

### Per pose

- **02 welcome/invitation** — `waves warmly`, `waves hello`, `waves back`, `waves goodbye`,
  `waves at the`, `waves to the`, `waving hello`, `waving back`, `welcoming`, `invitation`,
  `greeting`, `calls the child over`.
- **03 guide/point** — `gestures encouragingly`, `gestures toward|along|ahead|onward`,
  `motions toward|ahead`, `signals toward`, `shows the child the way`, `leads the way`,
  `traces the path`, `marks the path`, `lights the path`, `guiding`.
- **04 listen** — `listen`, `hearing`, `attentive`, `tilts their|her|his|the head`, `head tilted`,
  `cocks their head`, `leans an ear`, `perks up`.
- **05 excited/celebrate** — `claps`, `clapping`, `clap`, `applauding`, `celebrate`, `happily`,
  `excitedly`, `excited`, `cheerful`, `in awe`, `with awe`, `awe and joy`, `grins`.
- **06 brave/protective** — `protect`, `shield`, `guarding`, `stands guard`, `steps in front`,
  `stands firm`, `shelters`, `keeps the child safe`, `braces`.
- **07 curious/lean** — `wonder`, `marvels`, `marveling`, `marvelling`, `leans in|forward|down`,
  `peers`, `peering`, `peeks`, `studies`, `looks closely`, `looks carefully`, `inspect`, `examine`,
  `investigate`, `intrigued`, `puzzled`.
- **08 gentle/reassure** — `reassure`, `comfort`, `encourages`, `encouraging`, `encouragement`,
  `stands beside|close|nearby|near`, `stays beside`, `sits beside`, `kneels beside`, `rests beside`,
  `rests peacefully|quietly|cozily|calmly`, `waits beside`, `nods`, `smiles warmly|gently|softly`,
  `watches warmly|warm-heartedly|thoughtfully|gently|quietly`, `watches over`, `watching over`,
  `warm and caring`, `caring expression`, `caring smile`, `gentle`, `gently`, `tenderly`.
- **09 forward/adventure glide** — `walks beside|alongside|with the child`,
  `steps alongside|beside`, `moves alongside`, `follows close behind`, `follows behind`,
  `follows the child`, `glides ahead|alongside`, `flies ahead`, `heads onward`, `sets out`,
  `travels onward`, `journeys onward`, `looks out`, `looking out`, `gazes out`, `gazing out`,
  `onward`.
- **01 neutral hover** keeps an empty list. It is reachable only as the fallback, which is what makes
  the fallback count meaningful.

### Collisions, and how the documented order resolves them

Because matching is plain substring, a sentence can hit two poses. `priority_order` decides, and
these are the cases the real books actually produce — each one asserted by a test:

| sentence | hits | resolves to | why |
| --- | --- | --- | --- |
| "Beki stands beside the child, **looking out** into the night sky." | 08 `stands beside`, 09 `looking out` | **08 reassure** | standing beside the child is the pose; the outward look is scenery |
| "Beki **points** happily toward the valley." | 03 `points`, 05 `happily` | **03 guide** | the page shows a direction being given |
| "Beki **celebrates** joyfully next to the child, **looking out** at the streets." | 05, 09 | **05 celebrate** | the beat is the celebration |
| "Beki **leans in curiously** to watch the child **examine** the lantern." | 07 twice | **07 curious** | `curious` is earlier in the pose's own list |
| "Beki welcomes the child and **bravely shields** her." | 06, 02 | **06 protective** | v1.0's own rule, unchanged |

### Words deliberately NOT added

Each of these would have matched more real sentences and mis-mapped others, and substring matching
gives no way to exclude the bad case:

- bare **`point`** / **`gesture`** — "the trail **pointed** out by the child" is the child pointing,
  and "an encouraging **gesture** to reassure" is a reassurance, not a direction.
- bare **`joy`** — it is inside "en**joy**ing", which turned a quiet evening walk into a celebration.
  `in awe` / `with awe` / `awe and joy` carry the same sentences without the trap.
- bare **`waves`** — the ocean theme has waves. Only the greeting phrasings are listed.
- bare **`hear`** — it is inside "warm-**hear**tedly", and pose 04 outranks pose 08, so a warm look
  would have become a listening pose. `hears` (v1.0) and `hearing` are safe.
- bare **`caring`** — it is inside "s**caring**".
- bare **`beside the child`** — it appears in both stationary sentences ("stands beside") and moving
  ones ("walks beside"), which are different poses. The verb decides, so the verb is in the keyword.
- bare **`brave`** — pose 06 is the highest priority in the table, and "as the child bravely steps
  forward" is about the child. Pose 06 gained only phrases whose subject can only be Beki.
- **`walks joyfully`** (pose 09) — added, then removed before shipping: pose 05's v1.0 `joyful` is a
  substring of "joyfully" and pose 05 outranks pose 09, so the entry could never win and would have
  been a dead line that read as coverage. A test asserts every listed keyword is reachable.

Known accepted imprecision: `wonder` also matches "wonderful" and "wonderland", and `sound` /
`adventure` (both v1.0) are broad in the same way. Curious-lean is a defensible picture for all of
them, and a false match still beats the fallback.
