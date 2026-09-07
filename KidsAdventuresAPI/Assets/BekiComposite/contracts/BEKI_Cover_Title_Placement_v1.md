# BEKI Cover Title Placement

**Version:** 1.0 (`cover-title-placement-v1`)
**Date:** 2026-09-08
**Status:** Deterministic amendment to the fixed title-safe rectangle in `BekiCoverDieline`
**Implementation:** `Services/Story/Composite/BekiCoverTitlePlacement.cs`
**Shared reading:** `Services/Story/Composite/BekiActivityMap.cs` (with `BekiPlacementChooser`)

## 1. The observed defect

Owner, 2026-09-08: *"On a production book the cover TITLE was printed over the child's face."*

It was. `BekiCoverDieline.TitleSafe*` is a fixed rectangle — x 285.5, y 34, 136 × 46 mm, the upper
front board — and `BekiPdfComposer` set the title there on the press wrap and, through
`InsideFrontBoardCrop`, on the customer's front page as well. Every book ever composed put its name
in that one place.

Two things were supposed to stop this and neither could.

- **The prompt.** `BekiCoverDieline.ReservedArtworkInstructions` tells the image model the title's
  rectangle in millimetres — "TITLE: x=285.5..421.5, y=34..80" — and asks it to keep the child's
  face, head and hairline outside it. A model cannot measure a millimetre on a canvas it is
  composing freehand; on the book the owner opened, the child's head is in the band.
- **The gate.** `BekiCoverLayoutSafety` compares HUMAN-recorded bounds against that same fixed
  rectangle, and production records `NOT_REVIEWED`. Under the owner's rule 5 of 2026-09-01 — *"we
  don't need additional reviews for images"* — no vision model looks at a cover either. Nothing
  automatic had ever looked at the picture.

## 2. Authority

The same clause that licensed `BEKI_Beki_Placement_Selection_v1`, supplier handoff §14:

> a failed placement should first adjust deterministic anchors, not redraw Beki.

Applied to the other thing this pipeline places by number. It is arithmetic over bytes the book has
already paid for: **no model is asked anything**, no image is bought, and the whole measurement
costs about 0.3 s on a 1536-wide wrap and 0.4 s on the 6047-wide press raster, once per book.

## 3. What it never does

- **Never resizes the title box.** It stays 136 × 46 mm, so `BekiPdfComposer.CoverTitleSizePt`'s fit
  ladder — the answer to the 2026-09-07 "the book name is trimmed" defect — measures against exactly
  the rectangle it always did, and a moved title breaks its lines in exactly the same places.
- **Never paints anything on the artwork.** It returns four millimetres.
- **Never lets the two covers disagree.** The press page and the customer's front page take the
  decision from one cache in the composer, keyed by the wrap's SHA-256. Audit P0-01's finding was
  two covers that were not the same design; a box chosen twice would be that again.
- **Never moves the title above the approved top, off the board, or under the logo.** See §5.
- **Never fails a book.** If the measurement throws for any reason, the approved rectangle stands —
  it is where the title has always been set and it is a legal, printable answer — and a warning
  names the wrap.
- **Never changes `BekiCoverDieline`'s constants or what the cover prompt reserves.** The prompt
  still asks the model for a calm upper band, and the composition manifest still records the
  reservation. This amendment is what happens when the model does not honour it.

## 4. The reading

`BekiActivityMap`, unchanged, shared with `BekiPlacementChooser`: one box-filter downscale of the
picture to **512 columns**, from which two maps are taken and mixed half and half — Sobel contour
strength on luminance, and Euclidean RGB distance from the picture's own mean colour, each divided
by its own 95th percentile and clamped. The mixture is raised to the power 1.5, spread over a box
6 % of the width across, and the map's own 5th percentile is subtracted. §4 of
`BEKI_Beki_Placement_Selection_v1.md` is the full description and it is the same code; it moved out
of `BekiPlacementChooser` into its own file for this amendment and `BekiPlacementChooserTests` is
the assertion that the move changed nothing.

**What it reads is the wrap COMPOSITE** — the base with the approved Beki already pasted on — which
is what the composer is handed. That is what makes Beki free of geometry here: she is part of the
picture by the time this looks at it, and a rectangle over her measures busy for the same reason a
rectangle over the child does. There is no pose arithmetic in this class and nothing to keep in step
with the engine.

## 5. The window

A candidate rectangle must satisfy all of:

1. **Left edge on one of four columns:** x ∈ {285.5, 300, 320, 340} mm. A short explicit list rather
   than a sweep, because the horizontal room is small and unevenly useful: the box is 136 mm wide in
   a 222.5 mm board with 16 mm margins, so the entire horizontal travel is 54.5 mm, and every
   millimetre of it walks the box under the logo.
2. **16 mm inside the board's left and right edges** (269.5–492 mm) and **16 mm above its foot**
   (225 mm) — the margin the approved box already keeps on the left, applied to the other three
   edges.
3. **Never above the approved top of 34 mm.** Above it is the turn-in; the window only ever goes
   down. Rows step by **8 mm**, from 34 to 162 — about a sixth of the box's height, fine enough to
   find the gap between a head and a shoulder, coarse enough that the board is 17 rows.
4. **Clear of the logo's visible rectangle plus its 8 mm clear space.** In practice this is what
   makes the three right-hand columns available only below y ≈ 61 mm.

A real cover therefore offers **56** rectangles, the approved one included.

## 6. The decision

Cost of a candidate = mean activity **under the box** + **0.5 ×** the mean over a **4 mm** band
around it + **0.20 ×** however far past 0.035 of the canvas the box has walked from the approved
one, distance measured in canvas fractions.

The box moves only if the best candidate is **materially** calmer: at most **0.85 ×** the approved
box's cost **and** at least **0.03** below it in absolute terms. Among candidates within 5 % of the
best, the one nearest the approved box wins, then a fixed sweep, so the same bytes always give the
same cover.

### The toll is a fifth of Beki's, and it does a different job

`BekiPlacementChooser.DepartureCost` is 0.60 and its job is to stop the calmest pixel of a whole
spread pulling Beki into an empty corner. There is no corner here: the window is four columns of one
board with the edges already excluded, and on a cover whose upper board the model filled, the honest
destination IS the calm band lower down. **Measured at 0.60 the toll refused every real move** — on
the production wrap it added 0.297 to a candidate whose whole advantage was 0.160 — so it is not a
shaper of the destination but a surcharge on the *decision to move at all*, added before the
0.85 ratio reads the two numbers.

One consequence is deliberate and should not be discovered by surprise. A step down the grid costs
only 0.0065 of toll, far less than the score differences between rows, so among the candidates that
clear the bar the calmest legal rectangle almost always wins. **The title moves rarely; when it
moves, it goes where the picture is quietest**, which on the production wrap is the board's lower
band (§8).

## 7. What is recorded

Both cover pages' layout receipts gain an optional `title_box`, trailing and null on every other page
in the book:

```json
"title_box": {
  "left_mm": 285.5,
  "top_mm": 162.0,
  "width_mm": 136.0,
  "height_mm": 46.0,
  "version": "cover-title-placement-v1",
  "moved": true,
  "reason": "moved the title off the approved box: it measures 0.276 against 0.214 at 285.5,162.0 mm — 23% calmer, chosen from 56 candidates."
}
```

Stated in WRAP millimetres on both pages alike, so the press receipt and the customer's receipt are
directly comparable rather than two coordinate systems; the customer's page derives its own padding
from these through `BekiCoverDieline.InsideFrontBoardCrop`. One Information line per cover carries
the same decision to the log, keyed by the wrap's hash.

`BekiCoverLayoutSafety.Conflicts(areas, titleBox)` now takes the box, defaulting to the approved one
— which is what every book printed before this amendment used. The admin cover-layout endpoint
returns the chosen rectangle when the pack's wrap composite exists, so a reviewer drawing "the head
is here" is drawing against the rectangle this book's title is actually set in.

## 8. Calibration

Two real wraps and one synthetic. Renders in `output/cover-title/` (`*-boxes.png` — approved box in
red, chosen in green, logo clear space in blue; `*-press-1.png` — the composed press cover at 60 dpi;
`*-activity.png` — the reading itself; `sweep-b-top*.png` — every candidate row on the production
wrap). The probe that produced them was temporary and is not committed.

| Wrap | Pixels | Approved score | Chosen box | Chosen score | Moved | Candidates |
|---|---|---|---|---|---|---|
| `b807042266124449915e3965a631b370` cover-wrap-composite | 1536 × 735 | 0.2656 | 285.5, 34 | 0.2656 | no | 56 |
| `54cba4b3` print/cover-wrap-composite | 6047 × 2894 | 0.2763 | 285.5, **162** | 0.2137 | **yes** | 56 |
| synthetic head in the top band | 1536 × 735 | 0.4480 | 285.5, **106** | 0.2378 | **yes** | 56 |

- **`54cba4b3` — the fix.** The approved box lands on the floating castle city and its right third on
  the girl's hair and forehead. The chosen box is on the open cloud she is sitting on, below her
  hands, clear of her entirely, over the calmest ground the front board has. Seen at 60 dpi with the
  real title set in it, the two lines read cleanly and the child is untouched. This is the defect
  answered.
- **The synthetic head.** A pastel sky with a dark head painted into the approved band, drawn only to
  show the mechanism on a picture whose defect is unambiguous. The title steps down four rows, just
  past the head's own 44 mm, onto clear sky.
- **`b807042266124449915e3965a631b370` — the honest failure, and it should be read.** The title stays
  where it was, over the child's hair with its lower edge at his eyebrows. That is not a tuning
  miss: on that cover the child is painted across the entire front board from the board's head to
  its foot, and **the approved box is the calmest 136 × 46 rectangle the board contains** — every
  other row measures worse, because every other row is more of the child's face and body. There is
  no rectangle to move to. See §9.1.

## 9. Known limits

1. **A window cannot invent a calm place that is not there.** On the 1536 wrap of §8 the whole front
   board is occupied, so the amendment correctly declines to move and the title still prints over
   the child's hair. Nothing downstream of generation can fix that cover; the fix is upstream, in
   what the cover prompt gets the model to compose, and this document is the evidence that the
   placement stage had nothing to offer. The decision and both scores are recorded, so such a cover
   is identifiable in its own receipt rather than only on the printed page.
2. **The reading is contour and colour, not "where is the child".** Dark hair against a dark night
   sky is nearly invisible to both halves — it is §9.4 of the Beki placement contract in a new
   place, and it is part of why limit 1 reads the way it does on that wrap.
3. **The toll's 0.20 is calibrated on two covers.** Anything at or above about 0.244 would have left
   the production wrap where it was; anything much smaller changes nothing, because the destination
   is already the calmest rectangle. It is one named constant with a doc comment saying what each
   direction costs, which is the most a calibration on two pictures can honestly claim.
4. **When it moves, it usually moves a long way.** §6 explains why: the toll is too light to stop at
   an intermediate row. On a cover whose only trouble is a head in the top band that is right — the
   synthetic case steps down exactly as far as it needs to, because the rows below the head are all
   equally calm and the near-best tie-break takes the nearest. On a cover whose calm increases
   steadily downwards, the title goes to the board's lower band. That is a real design change, it is
   recorded as one, and it is the one thing here a reviewer should look at rather than take on the
   arithmetic's word.
5. **`BekiPackFulfillment`'s own cover-layout gate still reads the approved rectangle.** The gate
   call at the release stage passes no box, so it takes the default. On a book whose title moved, a
   recorded head under the OLD rectangle would still be reported as a TITLE conflict there. The
   composer's own `EnsureClear` and the admin endpoint both use the chosen box; the fulfilment call
   site was out of this change's scope and is a one-argument follow-up.
6. **Nothing re-reads a stored decision.** The box is recomputed from the wrap whenever a cover is
   composed. That is safe because the reading is deterministic and the wrap's hash keys the cache,
   but it does mean a future change to this algorithm would place an old book's title differently on
   a reprint. The receipt records the version for exactly that reason.

## 10. Relationship to other documents

Nothing here supersedes a supplier document. The Locked Print Specification's dieline is unchanged,
`BekiCoverDieline`'s constants are unchanged, and the approved rectangle remains the answer on every
cover that has no better one. This amendment adds a measurement between the dieline and the composer,
and a record of what it decided.
