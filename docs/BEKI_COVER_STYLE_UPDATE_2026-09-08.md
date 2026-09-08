# Cover style update — 2026-09-08

Owner request overrides the older colored cover-logo treatment.

- Cover logo: the hash-locked `HiResColor.svg` path geometry is painted solid white as
  native PDF paths. The original asset is not modified; this is a cover-only ink override.
- Visible logo width: 39.6 mm (36 mm × 1.10), proportional height approximately 14.07 mm.
  Right/top physical fold offsets remain 20 mm. The title exclusion area uses these new bounds.
- Cover title: display-only uppercase, including Georgian Mtavruli, in the existing licensed
  Ottia face. Fitting, line receipts, reader-cover rendering and outline scaling use the same
  displayed title. Stored book names and interior text retain their original casing.
- Cover prompt version: `cover-child-world-v1.4`. The logo area is requested as naturally
  calm and sufficiently dark for the new white logo; no artificial panel is requested.
- Existing delivered books are not rebuilt or regenerated.

## Pending font choice

The repository contains licensed Ottia Regular only, not a heavier Ottia cut. Inspection with
`fc-query` confirms Regular and both Mkhedruli and Mtavruli glyph ranges. A genuine heavier
Ottia font file (with permission to embed it), or owner approval to substitute the already
available Noto Sans Georgian Bold, is required to finish the heavier-weight request. Merely
asking the renderer for Bold on the Regular-only face is not claimed as a font replacement.

Verification uses offline synthetic artwork, never paid image generation or an old customer book.

## Verification

- Focused cover/PDF/reader/interior regression suite: 126 passed, 2 opt-in tests skipped.
- Synthetic cover proof rendered with Poppler and visually inspected: full Mtavruli title,
  white vector logo, clear separation. `pdffonts` confirms embedded/subset Ottia; `pdftotext`
  extracts the complete title as Mtavruli Unicode, not a fallback font or rasterized text.
- Proof: `output/pdf/cover-white-mtavruli-2026-09-08.pdf` (local diagnostic, not customer artwork).
- Deployment completion is separate from the source push and is not asserted here.
