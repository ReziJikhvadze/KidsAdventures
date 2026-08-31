# BEKI Deliverables Audit and Claude Correction Brief

**Audit version:** 1.0  
**Audit date:** 2026-08-31  
**Current release verdict:** **REJECT — not ready for print production or customer delivery**  
**Scope:** Full inspection of the supplied delivery package and the supplied customer PDF.

## 1. Files audited

| Item | SHA-256 | Result |
|---|---|---|
| `beki-da5e9f74-e7a5-4f87-b745-f07f16b6cc5b-package.zip` | Package CRC test passed | Archive is structurally valid |
| `reading-copy.pdf` | `8a41310959e15b1abcbee08d7fe78b6810b1770c3d29325d3e53cd205b6a3a67` | Same bytes as the separately uploaded Georgian-title PDF |
| `press/interior.pdf` | `ac1186fbf95b5926413a4f9fcf88c108d8914718a2d1544a870bcf0e9dde845c` | Technically readable, but visually not releasable |
| `press/cover.pdf` | `e650a8f4f971fac909cce1ed19fd249ccc50408d8989ec17d4ce9ed0f98d8d06` | Correct geometry, but visually and technically not releasable |

The separately uploaded PDF and the package's `reading-copy.pdf` are byte-for-byte identical. Findings are therefore not duplicated.

## 2. Executive summary

The package contains several important technical improvements: the press PDFs are identified as PDF/X-4, use the correct FOGRA39 Output Intent bytes, contain no RGB raster objects, embed the intended fonts, use vector text and a vector QR, and have the locked page geometry. The story Beki composition receipts also match the supplied story assets.

However, the release must be rejected because the final artifacts are assembled through conflicting pipelines and the final QA gates are not functioning. The most serious failures are:

1. The print and customer covers are different designs. The customer cover uses a separately AI-redrawn Beki, while the print cover uses an exact composited Beki.
2. The customer back cover is a flat purple placeholder, not the back-panel crop of the canonical cover environment.
3. The print cover artwork is only about 125 effective PPI and contains visible tonal bands aligned with the spine construction.
4. Multiple story bases contain a measurable image discontinuity exactly at the center fold. Story Spread 4 also contains a large rasterized rectangular wash crossing the fold.
5. CMYK conversion changes intended light text to black. The credits text becomes nearly invisible on the dark background.
6. Story source images are only about 143 effective PPI and are merely upscaled to nominal 300 PPI inside the press PDF.
7. Final page-level QA files are missing for all eight story spreads even though the package was released. The review explicitly says `needs_human_reading: true`, but this does not block release.
8. The cover composition receipt names and hashes an output file that is absent from the package.
9. The approved intro, pattern, Beki, font, and color assets are not governed by a single canonical hash manifest, which allows old or fallback assets to be selected silently.

Claude must fix the pipeline, not only retouch the current PDF.

## 3. Requirements baseline used for this audit

### Press interior

- 12 landscape spreads.
- MediaBox and BleedBox: `450 x 210 mm`.
- Centered TrimBox: `440 x 200 mm`.
- 5 mm outer bleed.
- One uninterrupted visual panorama per spread.
- The fold is a safety zone only; it must not be visible.
- No center seam, full-half tint, overlay boundary, fade boundary, or fold marker.
- Story text: licensed Noto Sans Georgian, as a vector layer.
- Text support: a soft cream wash sized to the wrapped copy, not a full half-page panel.
- Exactly one vector QR on the credits spread, linking to `https://beki.ge`.
- PDF/X-4, exact Coated FOGRA39 Output Intent, all raster objects CMYK.

### Press cover

- Hardcover wrap, `512 x 245 mm`.
- Horizontal construction: `20 + 222.5 + 8 + 11 + 8 + 222.5 + 20 mm`.
- Vertical construction: `20 + 205 + 20 mm`.
- MediaBox, CropBox, BleedBox, and TrimBox all `512 x 245 mm`.
- One continuous cover world.
- Back panel environment-only; child and exact approved Beki on the front panel only.
- No visible hinge/spine guides, no spine title, no varnish layer in v0.
- Licensed Ottia title as vector text.

### Customer reading PDF

- One PDF containing: front cover, opening endpaper, intro, eight story spreads, credits, rear endpaper, back cover.
- Front and back covers must be crops derived from the same canonical cover master as the press cover.
- No full printer wrap, spine, hinges, turn-ins, bleed, crop marks, or printer-only page boxes.
- Trim-size customer pages: front/back `220 x 200 mm`; landscape spreads `440 x 200 mm`.
- sRGB, screen-optimized, vector text and QR, full uninterrupted landscape spreads.
- The customer PDF must be rendered from the same approved layout state as the press PDFs, not assembled from a separate cover or legacy assets.

## 4. What is correct and should be preserved

| Area | Verified result |
|---|---|
| Archive integrity | ZIP CRC test passes; no archive-path safety issue was found |
| Press interior geometry | 12 pages; `450 x 210 mm` Media/Bleed and centered `440 x 200 mm` Trim on every page |
| Press cover geometry | One `512 x 245 mm` page; all four principal page boxes match the locked size |
| PDF/X identification | Both press PDFs contain `pdfxid:GTS_PDFXVersion="PDF/X-4"` |
| Output Intent | Embedded ICC SHA-256 is exactly `b35713ef7eff09349d4c3249e5f377736d06d8a2671c54712971a3546bf17c57` |
| Raster color spaces | Press raster objects are CMYK; the earlier intro RGB defect is not present |
| Fonts | Noto Sans Georgian is embedded/subset in the interior; Ottia is embedded/subset in the cover |
| Vector content | Story/intro/credits text remains vector; the QR is vector, not baked into an illustration |
| QR count and location | Exactly one QR is present, on the credits spread; Story Spread 8 has no QR |
| QR destination | The vector module matrix exactly matches a Version 2, error-correction Q, mask 6 QR for `https://beki.ge` |
| Story Beki receipts | All eight base/output hashes match their receipts; receipts declare no mirror, rotation, warp, or redraw |
| Story sequence | Opening, intro, Story 1–8, credits, and rear endpaper appear in the intended order |
| Text-side rhythm | Story text alternates left/right consistently and stays away from the outer trim edges |
| Render validity | All PDFs fully interpret in Ghostscript and render in Poppler without parser failure |

These passes do not override the visual and workflow blockers below.

## 5. Blocking defects — P0

### P0-01 — Two incompatible cover pipelines are being shipped

**Evidence**

- `reading-copy.pdf` page 1 uses the separate front-only `cover/cover.png` artwork.
- `fulfilment-manifest.json` describes that file as `cover-identity-redraw-v1.4`, sets `isRedraw: true`, and marks it `PASS`.
- The Beki visible in this customer cover is integrated into the AI redraw and is not the exact approved transparent Beki asset.
- `press/cover.pdf` uses a different wrap background and a separately composited exact Beki pose.
- The child, palette, Beki placement, title placement, and overall composition differ between the two versions.

**Why it fails**

The customer and print products do not represent the same approved book. The customer cover also violates the exact-Beki rule.

**Required correction**

Create one canonical cover master only:

1. A continuous environment/child wrap base with no generated Beki and no title.
2. Composite the exact approved transparent Beki PNG without mirroring, rotation, warping, or redraw.
3. Add the Ottia title as vector text.
4. Produce the press cover from that master.
5. Crop the front and back board panels from that same master for the customer PDF.
6. Remove `cover.png` from final-output selection; it may remain only as a rejected historical preview.

### P0-02 — The customer back cover is a placeholder

**Evidence**

`reading-copy.pdf` page 14 is a flat dark-purple page with only `beki.ge`. It does not match the environment-only back panel in `press/cover.pdf`.

**Required correction**

Use the canonical cover master's back board-panel crop. Do not use a flat color placeholder, generated Beki, draft wordmark, spine, hinges, or turn-ins.

### P0-03 — Visible cover construction bands are baked into the artwork

**Evidence**

`cover-wrap-base.png` has strong vertical tonal jumps at approximately x=1236 and x=1291 px. On a 2528 px-wide, 512 mm cover these locations correspond to 250.5 mm and 261.5 mm — the exact spine boundaries. The final cover visibly shows dark/tinted vertical bands and an abrupt warm-green-to-purple transition.

**Why it fails**

Hinge and spine geometry may guide placement internally, but it must not be rendered as visible artwork. The cover also fails the one-continuous-world requirement.

**Required correction**

Regenerate or rebuild one continuous panorama. Keep the 27 mm center construction low-information, but do not add guide overlays, panel tints, blur zones, or visible boundaries. Dieline guides must exist only in a non-printing diagnostic layer or external QA preview.

### P0-04 — The print cover is only about 125 effective PPI

**Evidence**

- Embedded cover raster: `2528 x 1210 px`.
- Physical placement: `512 x 245 mm`.
- Effective resolution: approximately `125.4 x 125.4 PPI`.
- A 300 PPI cover at this geometry requires approximately `6047 x 2894 px`.
- `cover-preflight.json` does not test effective placed resolution and incorrectly allows the cover to pass.

**Required correction**

Generate or high-quality super-resolve the canonical cover master to at least `6047 x 2894 px` before PDF assembly. Do not pass a file merely because it has 300 PPI metadata. Validate pixel dimensions against physical placement.

### P0-05 — Multiple story spreads contain a center-fold discontinuity

**Evidence**

The supplied raster bases contain an abnormal pixel jump exactly at x=1264/1265, the 50% fold coordinate of a 2528 px-wide image. The center boundary is among the strongest vertical discontinuities in Story Spreads 1, 2, 3, 5, and 7. This is present in the source PNGs before PDF conversion.

**Why it fails**

The fold is visibly encoded into the art. Upscaling and CMYK conversion cannot remove the source defect.

**Required correction**

Do not build, prompt, extend, or shade the two page halves independently. Generate/repair one full-spread panorama. Add an automated centerline test and a human full-spread render review. A natural object may cross the fold, but there must be no full-height change aligned to exactly 50% of the canvas.

### P0-06 — Story Spread 4 contains a large rasterized rectangle across the fold

**Evidence**

`bases/spread-04-base.png` already contains a large semi-opaque white rectangle from approximately x=991 to x=1606 px. It crosses the center fold and remains in both customer and press PDFs. The actual story text is placed elsewhere on the right, so the rectangle is also functionally misplaced.

**Required correction**

Regenerate Story Spread 4 from clean art with no embedded text box or placeholder. Text support must be a separate local layout shape sized to the final wrapped text, must remain on the selected text page, and must not enter the fold safety zone.

### P0-07 — CMYK conversion destroys intended text colors

**Evidence**

- In the customer PDF, credits text is cream/white on dark purple.
- In the press PDF, the same text is converted to `0 g` black and becomes nearly invisible.
- Story and intro text similarly changes from light text in the customer PDF to black text in the press PDF.
- The preflight describes a global `BlackText` preservation/coercion step.

**Likely root cause**

A global Ghostscript black-text option or equivalent post-layout conversion is forcing text objects to black rather than preserving their authored color.

**Required correction**

- Remove global text-to-black coercion.
- Prefer preconverting final raster assets to FOGRA39 CMYK, then assemble the PDF with explicitly authored CMYK vector colors.
- Use intended dark text on localized cream story washes.
- Use light cream/white vector text on the dark credits background.
- Render and compare press/customer versions before release; color conversion must not change semantic design roles.

### P0-08 — Customer PDF still contains printer bleed geometry

**Evidence**

- Front/back MediaBox: `230 x 210 mm`; TrimBox: `220 x 200 mm`.
- Landscape MediaBox: `450 x 210 mm`; TrimBox: `440 x 200 mm`.
- CropBox is absent, so ordinary viewers display the MediaBox/bleed area.

**Required correction**

Build the customer PDF as a dedicated trim-size export from the canonical master: `220 x 200 mm` front/back and `440 x 200 mm` spreads. Do not carry print bleed, trim metadata, or printer-only geometry into the downloadable file.

### P0-09 — Final QA is missing but release proceeds

**Evidence**

- `PACKAGE_CONTENTS.json` lists all eight `qa/spread-XX-qa.json` files as missing.
- No page-level final QA verdicts or failure images are supplied.
- `composite-review.json` says `needs_human_reading: true`.
- The package nevertheless contains final press and customer PDFs.
- Preflight JSON says Poppler and QR scan were not run in that stage.

**Required correction**

Any missing mandatory QA artifact, `needs_human_reading: true`, unresolved P0/P1 finding, failed render, or unreadable QR must stop final assembly. A final package may not be marked complete while a required gate is unknown.

### P0-10 — Cover composition evidence is broken

**Evidence**

`cover-composition.json` declares output `cover-wrap-composite.png` with SHA-256 `6db68c9f9e8c73c60abbeb7914edddfa5cfac988047f3bd1868275923bbff64b`, but that file is absent. The package instead contains an unrelated `cover.png` with SHA-256 `caea634a4897168facd56dbed9c68be9d456cbd59d540b2d6bfe4755885b4bed`.

**Required correction**

Include the exact canonical composite named in the receipt and verify its hash before cover PDF generation. Hard-fail if the receipt output is missing or mismatched.

## 6. Serious defects — P1

### P1-01 — Story source detail is approximately 143 PPI, not true 300 PPI

The story PNGs are `2528 x 1180 px` for a `450 x 210 mm` spread, approximately 142.7 PPI. The press PDF embeds `5315 x 2480 px` images, but these are upscaled versions. Upscaling changes pixel count, not source detail.

**Correction:** create genuine 300 PPI story masters (`5315 x 2480 px`) or use approved super-resolution followed by a 100% physical proof inspection. Preflight must report source effective PPI, not only the embedded raster dimensions.

### P1-02 — Approved intro and pattern versions cannot be proven

The PDFs contain an intro and endpaper pattern, but the ZIP does not include the canonical approved intro background, pattern source, their versions, or hashes. The current pattern visually resembles the approved cream watercolor motif, but exact approval cannot be cryptographically verified. The intro cannot be verified against the latest approved source.

**Correction:** add a fail-fast `asset-lock-manifest.json` with exact role, version, filename/object ID, SHA-256, dimensions, color profile, and approval status for every fixed asset. Never fall back to a legacy/default file.

### P1-03 — Intro and credits lack complete composition receipts

There is no receipt proving the exact Beki pose/source/hash and placement used on the intro and credits spreads. The story has such receipts; these fixed pages do not.

**Correction:** use the same receipt contract for intro, credits, endpaper, and cover assets.

### P1-04 — Text-support treatment is inconsistent

Most story pages have text directly over artwork; Story Spread 4 has an oversized misplaced panel; the intro has no controlled local support. This does not implement the approved dynamic text-zone system.

**Correction:** after final text wrapping, calculate one local soft cream wash with approximately 6–8 mm internal padding. Keep it within the selected page, outside the fold safety area and trim safety margins. Do not bake it into the illustration and do not use a fixed half-page mask.

### P1-05 — Shot rhythm is not being followed

The manifest prescribes a varied sequence, but the generated book repeatedly uses the same central-tree environment and similar medium framing.

| Story spread | Required shot | Actual issue |
|---|---|---|
| 1 | Wide establishing, child small | Framing is closer; package reviewer also flags it |
| 2 | Medium discovery | Generally acceptable |
| 3 | Full-figure action | Child's feet are cropped; package reviewer flags it |
| 4 | Dramatic atmospheric wide | Medium framing plus a large raster panel; package reviewer flags it |
| 5 | Wide travelling with depth | Child remains large; journey depth is weak |
| 6 | Medium-wide major reveal | The giant magical tree/reveal is not visually distinct enough |
| 7 | Close emotional beat | Generally the strongest match |
| 8 | Cinematic wide, characters small | Characters remain medium-sized; final vista is weak |

**Correction:** make shot instruction compliance a scored QA gate, not an advisory that can be ignored.

### P1-06 — Child age/identity consistency remains unresolved

The package itself flags Story Spreads 1, 5, and 7 as appearing younger than the source. The print cover child also differs visibly from the customer cover and story child. Yet the cover is marked `PASS` and the review remains non-blocking.

**Correction:** require a human identity/approximate-age approval for the final cover and contact sheet. Privacy-sensitive reference files may remain in protected storage, but the run manifest must carry a secure asset ID and checksum rather than silently omitting the identity dependency.

### P1-07 — Story continuity and scene-to-text mismatches need correction

- Story Spread 4 says the pinecone light is fading, but the image still shows a strongly glowing pinecone.
- Story Spread 8 no longer visibly carries forward the pinecone with the sleeping bear.
- Bear-cub proportions and appearance shift between scenes.
- The story alternates present and past tense (`ქრება/ანათებს` versus `გამოვიდა/გაჰყვნენ/აინთო`) and includes the unnatural toddler phrasing `მას ძილი ნებავს`.

**Correction:** run a final Georgian editorial pass and a visual state-continuity check. For a two-year-old, prefer one simple tense and natural phrasing such as `მას ეძინება` if approved by the Georgian editor.

### P1-08 — Visual Scenario page 7 contains malformed source text

`visual-scenario.json` begins page 7 with: `" sensitivity, the child gently pats..."`.

**Correction:** schema validation must reject leading fragments, malformed sentences, and missing words before image generation.

### P1-09 — Beki placement on the press cover reads as pasted over the child

The exact Beki asset overlaps the child's torso and its top curl reaches the face area. This weakens hierarchy and visual integration.

**Correction:** keep the exact asset unchanged but reposition it beside the child, clear of the face and torso, with a deliberate interaction and safe separation from title/spine/trim zones.

### P1-10 — Required reproducibility inputs are absent

The package does not include or checksum-lock the exact ICC file, locked production specification, licensed font source files, approved Beki pose sources, normalized Story JSON, intro source, pattern source, or final render proofs. Embedded subsets inside PDFs are not sufficient to reproduce a new book.

**Correction:** include these files when licensing/privacy permits; otherwise include immutable secure references plus SHA-256 and access requirements. The normalized Story JSON must be part of the run handback even if it is also stored in a master run record.

## 7. Improvements — P2

1. Optimize the customer PDF for screen delivery. It is currently 33,985,705 bytes and is not linearized. Target a visually approved sRGB export around 144–180 PPI and enable Fast Web View; do not downsample the press masters.
2. Add document language `ka-GE`, logical reading order/tagging where practical, and useful PDF bookmarks/metadata for the customer copy.
3. Use explicit, collision-resistant filenames, for example:
   - `BEKI_<book-id>_PRESS_COVER_v001.pdf`
   - `BEKI_<book-id>_PRESS_INTERIOR_v001.pdf`
   - `BEKI_<book-id>_DIGITAL_READING_v001.pdf`
4. Add a complete final checksum manifest containing every delivered file, size, MIME type, SHA-256, build ID, and canonical/diagnostic status.
5. Record repository commit SHA, runtime/container version, model/provider IDs, prompt versions, retry counts, generation latency, and exact conversion commands.
6. Include Poppler and Ghostscript render logs and rendered contact sheets in the QA folder.

## 8. Page-by-page final-PDF findings

| Press interior page | Role | Status | Finding |
|---:|---|---|---|
| 1 | Opening endpaper | Conditional pass | Correct left-pattern/right-blank structure; canonical approved source/hash is missing |
| 2 | Intro | Revise | Correct structural role and Noto vector text; approved background and exact Beki source are not provable; local text support/contrast is not controlled |
| 3 | Story 1 | Revise | Content matches; center seam at 50%; establishing shot too close; age advisory unresolved |
| 4 | Story 2 | Revise | Content matches; center seam at 50% |
| 5 | Story 3 | Revise | Content matches; center seam at 50%; feet cropped despite full-figure requirement |
| 6 | Story 4 | Reject | Large raster rectangle crosses fold; wrong shot scale; pinecone still appears strongly lit |
| 7 | Story 5 | Revise | Center seam at 50%; travelling shot too close; age advisory unresolved |
| 8 | Story 6 | Revise | Main action present, but the major magical-tree reveal is weak; text support inconsistent |
| 9 | Story 7 | Revise | Emotional beat works; center seam at 50%; age advisory unresolved |
| 10 | Story 8 | Revise | Bright path is present, but final shot is not cinematic/wide enough and pinecone continuity is weak |
| 11 | Credits/QR | Reject | Credits text becomes black on dark purple and is nearly unreadable; QR itself is correct |
| 12 | Rear endpaper | Conditional pass | Correct full-pattern role; canonical approved source/hash is missing |

## 9. Package and manifest findings

| Deliverable | Current state | Required state |
|---|---|---|
| Story base/composite receipts | Eight supplied; hashes valid | Preserve |
| Cover composite receipt | Output file missing | Include exact output and verify hash |
| Page-level visual QA | Missing for all eight spreads | Mandatory PASS/FAIL JSON plus failure artifact when failed |
| Human review gate | `needs_human_reading: true`, ignored | Must block release until resolved |
| Normalized Story JSON | Excluded | Include in run handback |
| Approved fixed-asset manifest | Missing | Add exact hashes and versions; fail on fallback |
| Intro/pattern source assets | Missing | Include or secure-reference with hashes |
| Licensed font source hashes | Missing | Record exact files/hashes and controlled access |
| ICC file/spec | Referenced but not packaged | Include exact locked ICC and production specification |
| Final file checksums | Missing from package manifest | Add every final output and QA artifact |
| Poppler/GS/QR evidence | Checks deferred or not recorded | Run on stored final artifacts and package logs |
| Build provenance | Incomplete | Commit SHA, environment, commands, model/prompt/retry metadata |

## 10. Required pipeline redesign

### 10.1 Canonical asset lock

Create `asset-lock-manifest.json`. Every fixed asset must have one role and one canonical hash. Suggested roles:

- `endpaper_pattern_final`
- `intro_background_forest_final`
- `intro_beki_pose_07_final`
- `cover_beki_pose_final`
- `credits_beki_pose_final`
- `ottia_regular_licensed`
- `noto_sans_georgian_regular_licensed`
- `fogra39_output_intent`

No code path may select assets by an ambiguous filename, newest timestamp, directory enumeration order, cached URL, or fallback default. Missing/mismatched assets must produce `ASSET_LOCK_FAILED`.

### 10.2 One canonical layout state

Build one logical book/layout manifest containing approved art, vector text, text-wash geometry, QR, and cover composition. Derive all outputs from it:

- press interior;
- press cover;
- customer reading PDF.

Do not run independent print and customer design pipelines.

### 10.3 Clean story art and local text layers

- Generate each story image as one full-width panorama.
- Do not request or bake text boxes, page-half overlays, fold marks, or center gradients into the image.
- Composite exact Beki from the locked PNG.
- Add text and the dynamically sized cream wash only in layout.
- Keep the wash entirely on the chosen page and outside the fold safety zone.

### 10.4 Resolution-aware print preparation

- Validate effective source PPI before PDF assembly.
- Cover minimum: `6047 x 2894 px` for the locked 512 x 245 mm canvas at 300 PPI.
- Interior minimum: `5315 x 2480 px` for the locked 450 x 210 mm canvas at 300 PPI.
- Hard-fail when detail is created only by ordinary interpolation.

### 10.5 Color-safe PDF assembly

- Keep working art in tagged sRGB until approved.
- Convert final raster art to the locked FOGRA39 CMYK profile.
- Assemble vector text/QR with explicit final colors.
- Avoid a global post-layout operation that forces every text object to black.
- Embed the exact Output Intent and validate its bytes.

### 10.6 Real release gates

The final package must not exist unless all gates pass. Advisories that affect an explicit requirement must become failures or require signed human approval.

## 11. Acceptance criteria Claude must satisfy

### Visual and asset gates

- [ ] Customer front/back are cropped from the exact canonical press-cover master.
- [ ] No AI-generated/redrawn Beki appears in any final output.
- [ ] Every Beki pose hash matches the approved asset registry; no mirror/rotation/warp/redraw.
- [ ] No visible center seam on Story Spreads 1–8.
- [ ] No rasterized text panel or full-half overlay on any story base.
- [ ] No visible hinge/spine/turn-in bands on the cover.
- [ ] Story text wash is local, copy-sized, and outside the fold safety zone.
- [ ] Shot sequence and critical prop/character states pass final review.
- [ ] Child approximate age and cross-page identity receive human approval.

### Press PDF gates

- [ ] Cover: one page, 512 x 245 mm, all page boxes correct.
- [ ] Interior: 12 pages, 450 x 210 mm Media/Bleed, centered 440 x 200 mm Trim.
- [ ] PDF/X-4 identification present.
- [ ] Output Intent ICC SHA-256 equals `b35713ef7eff09349d4c3249e5f377736d06d8a2671c54712971a3546bf17c57`.
- [ ] Every raster object is CMYK.
- [ ] Effective source resolution is at least 300 PPI at placement size.
- [ ] Noto and Ottia are embedded with no substitution.
- [ ] Text and QR remain vector.
- [ ] Credits text remains light and readable after CMYK conversion.
- [ ] Ghostscript and Poppler render the stored final PDFs without errors.
- [ ] QR scans from the rendered final PDF and resolves to `https://beki.ge`.

### Customer PDF gates

- [ ] 14 pages in the required order.
- [ ] Front/back are 220 x 200 mm; spreads are 440 x 200 mm.
- [ ] No bleed, wrap, spine, hinge, turn-in, crop mark, or printer-only box is visible/present.
- [ ] Tagged sRGB raster content; vector Noto/Ottia text and vector QR.
- [ ] Visual content, line breaks, text colors, and asset versions match the canonical master.
- [ ] Screen-optimized and linearized without harming text or QR clarity.

### Handback gates

- [ ] Exact cover composite exists and matches its receipt.
- [ ] Page-level QA JSON exists for every story spread and fixed page.
- [ ] `needs_human_reading` is false or has an explicit signed resolution.
- [ ] Normalized Story JSON and Visual Scenario JSON are included.
- [ ] Fixed-asset lock manifest and all allowed source assets/references are included.
- [ ] Final checksum manifest covers every delivered file.
- [ ] Build provenance and renderer/QR logs are included.

## 12. Exact implementation instruction for Claude

Use this audit as the authoritative correction backlog. Do not patch only the current PDFs and do not mark the package complete until every P0 item is fixed and every acceptance gate is evidenced.

Required delivery order:

1. Implement canonical asset locking and fail-fast asset resolution.
2. Replace the two-cover workflow with one canonical wrap master and derive digital crops from it.
3. Regenerate/repair the cover at true 300 PPI with no visible construction bands.
4. Regenerate/repair story bases that contain center seams, the Story Spread 4 panel, shot failures, or continuity failures.
5. Move all text support into dynamic vector/layout layers.
6. remove global text-to-black coercion and rebuild the CMYK workflow.
7. Rebuild press cover, press interior, and customer reading PDF from one canonical layout state.
8. Run all automated and human gates against the stored final artifacts.
9. Return the complete reproducible package, not only three PDFs.

For every correction, return:

- changed code paths and repository commit SHA;
- root cause;
- before/after artifact hashes;
- exact test executed;
- test result and evidence path;
- any unresolved limitation.

The release status must remain `REJECTED` until all P0 gates pass.

