# BEKI Beki Placement Selection

**Version:** 1.0 (`beki-placement-v1`)
**Date:** 2026-09-07 (departure toll recalibrated same day — §6, §8, §9)
**Status:** Deterministic amendment to the story-spread anchors in `pipeline_config_v2.json`
**Implementation:** `Services/Story/Composite/Poses/BekiPlacementChooser.cs`

## 1. The observed defect

Book `54cba4b3-f4ea-4c4c-99d6-d497e6a835ac`, drawn under image prompt `v1.7`, composited the
approved Beki against the child's face or shoulder on **spreads 3, 5, 7 and 8** of eight.

Nothing was wrong with the composite engine. Every story spread was pasted at the configured
default for its text side — `story_defaults.LEFT` x 0.594, `story_defaults.RIGHT` x 0.406, both at
y 0.458, height 0.333 — and those three numbers are correct: they were measured off an approved
printed proof. What they are not is a statement about where the model chose to draw the child in
the next eight pictures. The spread prompt asks it to keep that area calm (`BekiReserveBlock`,
v1.7) and it often does not, and until this amendment nothing downstream measured whether it had.

Under the owner's rule 5 of 2026-09-01 — *"we don't need additional reviews for images"* — no vision
model looks at a spread, so the reviewer's `recomposite_beki` rung never runs. The configured anchor
was, in effect, the only placement decision a shipped book received.

## 2. Authority

Supplier handoff §14, quoted:

> a failed placement should first adjust deterministic anchors, not redraw Beki.

This amendment is that adjustment, made before the fact instead of after it. It is arithmetic over
bytes the book has already paid for: **no model is asked anything**, no image is bought, and the
whole measurement costs a few milliseconds per spread.

## 3. What it never does

- **Never redraws, mirrors, rotates, warps or recolours Beki.** It returns three numbers. The five
  capability switches in `pipeline_config_v2.json` stay false and the engine still has no code that
  could honour a true.
- **Never changes the composite arithmetic.** The chosen anchor goes in through the same
  `anchorOverride` the engine has always taken; sizing, rounding, resampling and the manifest are
  untouched.
- **Never adds a field to `composition_manifest_v1.schema.json`.** That schema is the supplier's and
  already records the anchor that was USED. Which anchor was *considered*, and why, is written into
  the spread's own QA document (§7), which is ours.
- **Never draws her larger than the approved proof.** The only alternative size on offer is smaller.
- **Never moves her closer to the centre fold, into the reserved text third, or off the canvas.**
  See the window in §5.
- **Never fails a book.** If the measurement throws for any reason, the configured anchor stands and
  a warning is logged — the behaviour that predates this amendment.

## 4. The reading

One downscale of the base to **512 columns** (box filter), from which two maps are taken and mixed
half and half:

| Reading | What it is | What it catches | What it misses alone |
|---|---|---|---|
| Contour strength | Sobel magnitude on luminance, divided by the picture's own 95th percentile, clamped to [0,1] | Foliage, architecture, glowing detail, a face's features | A flat knitted sweater; a white dog on a white cloud |
| Colour distinctness | Euclidean RGB distance from the picture's mean colour, normalised the same way | A child painted in blue, cream and dark brown on a warm cream wash | Anything in a scene with no dominant wash — it returns a flat map and lets the contour half decide |

The mixed reading is raised to the power **1.5** (loud over merely present), then **spread**: every
pixel becomes the mean over a box **6 % of the canvas width** across, computed from a summed-area
table. That spread is the step that matters. A drawn character is an *outline* around a *flat
interior*; scored on raw gradient the quietest rectangle in all eight real bases is the child's own
chest. Spreading a figure's cost outward from its own contour fills it in, and makes standing just
beside somebody nearly as expensive as standing on them.

Finally the map's own 5th percentile is subtracted and clamped at zero, so that scores are read
against *the calmest place this particular picture has to offer* rather than against an absolute
scale a pastel cloudscape can never reach. It is a shift, not a rescale: a picture that is busy
everywhere stays near zero everywhere and is therefore left alone (§6).

### The skin term was dropped

The plan proposed an optional secondary skin-likelihood term (YCbCr, Cb 77–127, Cr 133–173). It was
implemented, measured on the eight real bases, and **removed**. On spread 5, **87.7 %** of the
picture falls inside that chrominance box; on spread 6, 66.5 %. These pages are a warm cream and
gold wash from edge to edge, and a classifier that calls seven eighths of the sky "skin" is not a
secondary signal, it is a constant with noise on it. The colour-distinctness reading in §4 replaces
it and does the job the skin term was wanted for — the child is the least average-coloured thing on
a pastel page — without pretending to identify anybody.

## 5. The window

A candidate anchor must satisfy, in this order:

1. **Fully inside the canvas**, at the exact pixel rectangle the engine would paste (both of its
   half-to-even roundings repeated, not approximated).
2. **Clear of the reserved text third** — the same rule `CompositeDeterministicChecks.CompositeProblems`
   refuses a shipped composite on. `TextColumnShare` is 0.33, so the third is real.
3. **No closer to the centre fold than the configured anchor already sits.** The clearance is the
   configured anchor's own (`x − halfWidth − 0.5`, mirrored for right-text), floored at 3 %. That
   clearance is a print decision somebody signed off on; a placement improvement may not spend it.
4. **5 % off the left and right canvas edges, 8 % off the top and bottom** — where bleed and trim
   live.

Within that window: x every 4 % of the width, y every 5 % of the height, at the configured height
and at 0.30. The configured anchor is always a candidate. Duplicates that round to the same pixel
rectangle are scored once. In practice a story spread yields 140–190 legal candidates.

## 6. The decision

Cost of a candidate = mean spread-reading under Beki's **alpha-weighted silhouette** (not her
bounding box — scenery under an outstretched arm is not covered) + **0.5 ×** the mean over a margin
band 3 % of the width around her + **the toll** × however far past 0.12 of the canvas she has walked
from the configured anchor.

That last term is not decoration. Without it the calmest pixel in the picture wins outright and Beki
ends up alone in whichever corner the model left empty — measured on the eight real bases as a jump
to the extreme edge on four spreads out of eight.

### The toll is discounted on a page the model crowded

The toll exists to defend a composition somebody signed off. On a page where the model painted the
child straight through the configured anchor there is no composition left to defend — only the
memory of where one used to be — and charging full price to leave it is charging for something that
is not there. So it is charged in proportion to how much of the default is still worth defending:

```
toll = 0.60 × clamp((0.30 − configured score) / 0.30, 0.60, 1.0)
```

Full price (0.60) on a page whose configured anchor measures zero, falling to a floor of **0.36**
once that anchor measures 0.12 or worse. On the eight real bases every page but spread 2 is at the
floor; spread 2, the one the model composed as asked, pays about two thirds.

The floor is the load-bearing half. It is what stops the worst-measuring page from also being the
freest to wander, which is the failure this whole term was added to prevent. **0.60 is a measured
compromise, not a principle**, and the two neighbouring values say why: below about 0.55 spread 4
crosses its entire window to the aeroplane's tail, and at 0.62 and above spread 8 can no longer
reach the bedroom wall and settles back onto the child's sweater. See §9.1 and §9.2.

She moves only if the best candidate is **materially** calmer: at most **0.85 ×** the configured
anchor's cost **and** at least **0.03** below it in absolute terms. Two consequences are deliberate:

- A page whose reserved area the model *did* keep calm keeps the approved anchor. Existing books do
  not change.
- A page that is busy everywhere keeps it too. There is nowhere calm to move to, and shuffling her
  around a crowded spread trades a known composition for an unknown one.

Among candidates within 5 % of the best, the one **nearest the configured anchor** wins, then full
height over reduced, then a fixed sweep — so the same bytes always give the same page. Determinism
is not a nicety here: a reprint that placed her differently would make the manifest a receipt for a
page that no longer exists.

## 7. What is recorded

Every unreviewed spread's QA document gains a `beki_placement` object:

```json
"beki_placement": {
  "version": "beki-placement-v1",
  "moved": true,
  "default": { "visible_center_x": 0.594, "visible_center_y": 0.458, "visible_height": 0.333 },
  "chosen":  { "visible_center_x": 0.589, "visible_center_y": 0.530, "visible_height": 0.300 },
  "default_score": 0.1591,
  "chosen_score": 0.1310,
  "candidates_evaluated": 177,
  "reason": "moved Beki off the configured anchor: …"
}
```

and one Information line per spread carries the same decision to the log. On the reviewed path
nothing else changes: the `recomposite_beki` nudge starts from the manifest's anchor, so it composes
on top of whatever was chosen. A regenerated base resets the placement to null and is therefore
chosen afresh, which is correct — it is a different picture.

## 8. Calibration

Eight real bases, book `54cba4b3`, 1536 × 717, poses and text sides read from the stored
`spread-0N-composition.json` / `spread-0N-qa.json`. Renders in `output/beki-placement/`
(`spread-0N-before.png`, `spread-0N-after.png`, `contact-sheet.png`); the probe that produced them
was temporary and is not committed.

| Page | Text side | Pose | Default anchor | Default score | Chosen anchor | Chosen score | Moved | Candidates |
|---|---|---|---|---|---|---|---|---|
| 1 | LEFT | pose_09_forward_adventure_glide | 0.594, 0.458, 0.333 | 0.2097 | 0.653, 0.730, 0.300 | 0.1398 | yes | 144 |
| 2 | RIGHT | pose_07_curious_lean | 0.406, 0.458, 0.333 | 0.0969 | 0.406, 0.458, 0.333 | 0.0969 | no | 177 |
| 3 | LEFT | pose_04_listen | 0.594, 0.458, 0.333 | 0.1288 | 0.589, 0.330, 0.300 | 0.0732 | yes | 177 |
| 4 | RIGHT | pose_08_gentle_reassure | 0.406, 0.458, 0.333 | 0.2452 | 0.083, 0.230, 0.300 | 0.1598 | yes | 188 |
| 5 | LEFT | pose_03_guide_point | 0.594, 0.458, 0.333 | 0.1591 | 0.589, 0.530, 0.300 | 0.1310 | yes | 177 |
| 6 | RIGHT | pose_07_curious_lean | 0.406, 0.458, 0.333 | 0.2367 | 0.406, 0.458, 0.333 | 0.2367 | no | 177 |
| 7 | LEFT | pose_05_excited_celebrate | 0.594, 0.458, 0.333 | 0.2187 | 0.588, 0.730, 0.300 | 0.1045 | yes | 177 |
| 8 | RIGHT | pose_08_gentle_reassure | 0.406, 0.458, 0.333 | 0.2597 | 0.126, 0.247, 0.333 | 0.1258 | yes | 188 |

Read against the four spreads the defect was reported on:

- **Spread 3** — she leaves the aeroplane's engine cowling beside the child's hands and floats in
  open cloud above the nose. Clean fix.
- **Spread 5** — she leaves the child's hair and stands in the cloud bank to her left, clear of the
  figure entirely. Clean fix.
- **Spread 7** — she drops from the child's shoulder to the paving at her feet, off the face and
  readable against flat stone.
- **Spread 8** — **fixed at full height.** It is an interior: the text is on the right, so Beki's
  half is the bedroom, and the child fills nearly all of it. Under the flat toll the calmest thing
  she could afford was the child's own hair. Under the discounted one she can afford the bedroom
  wall above the nightstand, beside the headboard and clear of the child entirely, and at 0.333
  rather than the reduced height — which is where a person would have put her.

And against the two the amendment moved as a side effect:

- **Spread 1** — a step further from the fold and lower, onto the cloud the child is sitting on
  rather than the balustrade behind her. Better, and the same kind of place.
- **Spread 4** — **the one placement a reviewer should look at rather than take on trust.** She
  leaves the aeroplane's wing, where she stood beside the child's cheek at nearly the child's own
  scale, for the tail fin at x 0.083 — the outermost centre the window allows. Measured, that is not
  close: the tail reads at about a third of the wing's cost. Seen, she is planted on a solid gold
  fin, fully inside the bleed margin, and reads as a passenger rather than as someone stranded in
  empty sky. It is nevertheless the corner of the window, which is the shape of the failure the toll
  exists to prevent, and it is the price §6's 0.60 pays for spread 8.

Spreads 2 and 6 keep the configured anchor, which is the other half of the claim: the chooser is not
a shuffler, and the discount did not turn it into one.

## 9. Known limits

1. **The toll's floor is calibrated on one book.** §6's 0.60 was chosen because 0.55 and 0.62 both
   make a real page worse, on eight bases from a single title. Two adjacent settings changing two
   different spreads is a narrow margin, and a second book may well move it. It is one named
   constant with a doc comment saying what each direction costs, which is the most a calibration on
   eight pictures can honestly claim.
2. **§8 spread 4 sits at the corner of its window.** The reading says the aeroplane's tail is three
   times calmer than the wing, and it is right; but "the calmest place is the outermost place" is
   also what a corner jump looks like from the inside, and nothing here can tell the two apart
   except a person looking at the page. Every such placement is recorded with its scores.
3. **A third height rung was tried and rejected.** The plan proposed admitting a smaller 0.26 Beki
   on any page where the best standard-rung candidate still scored ≥ 0.5 × the configured anchor.
   Implemented and measured, that gate fires backwards: on a calm page the default is already good,
   so the best is not much better than it, the ratio is near 1, and the rung is admitted — it moved
   spreads 2 and 6, the two pages the whole design exists to leave alone, while spread 8, at 0.494,
   fell just under the gate and never saw it. A relative test cannot express "this page has nowhere
   good to go". The rung is not in the code.
4. **No subject detection.** The reading is contour and colour, not "where is the child". A white
   dog on a white cloud is invisible to both, and a page whose only calm place is on top of one will
   put her there. Every such case is recorded with its scores, which is what makes it reviewable.
5. **The colour reading assumes a dominant wash.** In a scene with none it flattens and the contour
   half decides alone. That is a graceful degradation rather than a wrong answer, but it does mean
   the two halves are not equally load-bearing on every page.
6. **The fold clearance is inherited from the configured anchor.** If a future `story_defaults` moved
   the anchor towards the fold, the window would widen with it. That is the intended coupling — the
   clearance is a print decision and lives in the config — but it is worth stating out loud.

## 10. Relationship to other documents

Nothing here supersedes a supplier document. `pipeline_config_v2.json` `story_defaults` remain the
anchors, and remain the answer on every page that has no better one. This amendment adds a
measurement between the config and the engine, and a record of what it decided.
