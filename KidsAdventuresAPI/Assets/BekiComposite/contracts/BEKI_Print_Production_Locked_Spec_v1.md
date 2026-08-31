# BEKI Print Production - Locked Specification

**Version:** 1.0  
**Status:** Authoritative for the current v0 implementation  
**Purpose:** Remove the false print blockers introduced by the earlier Developer Input Checklist.

> Installed verbatim from the product owner's delivery of 2026-08-31, alongside
> `print/BEKI_Coated_FOGRA39_OutputIntent.icc` (121,368 bytes,
> SHA-256 `b35713ef7eff09349d4c3249e5f377736d06d8a2671c54712971a3546bf17c57`, verified on install).

## 1. Supersession

For the current fixed v0 book format, the following items are **already defined** and must not be requested again from the printer:

- cover geometry and hardcover-wrap layout;
- final all-CMYK raster requirement;
- the exact Coated FOGRA39 output-intent profile used by the approved benchmark PDFs.

This document supersedes the corresponding unresolved rows in `BEKI_Developer_Input_Checklist_v1.md` and any handoff instruction that says to stop solely because those three inputs are unavailable.

Printer reconfirmation is required only if the physical format, paper construction, page count, binding, or printer changes.

## 2. Cover geometry - locked

**Binding:** hardcover wrap  
**Flat cover canvas / MediaBox:** `512 x 245 mm`

### Horizontal construction, back to front

| Segment | Width |
|---|---:|
| Left turn-in | 20 mm |
| Back board panel | 222.5 mm |
| Back hinge / groove | 8 mm |
| Actual spine | 11 mm |
| Front hinge / groove | 8 mm |
| Front board panel | 222.5 mm |
| Right turn-in | 20 mm |
| **Total** | **512 mm** |

The previously described **27 mm center zone** is the complete center construction:

`8 mm hinge + 11 mm spine + 8 mm hinge = 27 mm`

It is not a 27 mm printable spine.

### Vertical construction

| Segment | Height |
|---|---:|
| Top turn-in | 20 mm |
| Board-panel height | 205 mm |
| Bottom turn-in | 20 mm |
| **Total** | **245 mm** |

### Cover PDF boxes for v0

Match the approved benchmark cover exactly:

- `MediaBox`: 512 x 245 mm;
- `CropBox`: 512 x 245 mm;
- `BleedBox`: 512 x 245 mm;
- `TrimBox`: 512 x 245 mm.

Do not apply the interior 5 mm bleed geometry to the cover.

### Cover composition rules

- Generate and compose one continuous panoramic cover world across back, hinges, spine, and front.
- The child and Beki may appear on the front panel only.
- The back panel must be environment-only and must not duplicate the front-panel composition.
- Keep the hinge and spine area low-information and free of important faces, hands, text, and story actions.
- Do not blur the back panel, hinge area, or spine.
- No spine title in v0.
- No spot UV or varnish layer in v0.
- Use the project-supplied licensed Ottia for cover titles, exported according to the locked print workflow.

If this exact fixed geometry is unavailable in code, use `LAYOUT_FAILED`. Do not estimate or substitute dimensions.

## 3. Interior geometry - locked

- Full spread `MediaBox` / `BleedBox`: `450 x 210 mm`;
- centered `TrimBox`: `440 x 200 mm`;
- bleed: `5 mm` on every outer edge;
- one uninterrupted full-spread illustration per PDF page;
- no visible seam, overlay, fade boundary, or fold marker at the center;
- center fold is a safety zone only and must not be rendered visually;
- interior body font: project-supplied Noto Sans Georgian, superseding Futura 100 GEO;
- story text and QR remain separate vector layers.

## 4. Color workflow - locked

Working composition assets may remain tagged sRGB until all raster and vector layout work is complete.

For the final press PDFs:

- `require_all_cmyk = true`;
- every raster image object must be CMYK;
- convert only after final composition;
- embed the exact Coated FOGRA39 profile supplied with this specification as the PDF/X-4 output intent;
- fail print preflight if any raster image object remains RGB;
- do not treat the RGB object in the old intro benchmark as acceptable precedent - it is a known export defect.

### Locked ICC profile

**Filename:** `BEKI_Coated_FOGRA39_OutputIntent.icc`  
**Profile description:** `FOGRA39L Coated`  
**Profile class / color space:** CMYK output profile  
**Size:** `121,368 bytes`  
**SHA-256:** `b35713ef7eff09349d4c3249e5f377736d06d8a2671c54712971a3546bf17c57`

The profile was extracted byte-for-byte from the Output Intent embedded in the approved BEKI cover and interior benchmark PDFs. The extracted bytes and SHA-256 match across the checked cover and interior files.

## 5. Press PDF requirements - locked

- separate cover and interior press PDFs;
- PDF/X-4;
- embedded Coated FOGRA39 Output Intent using the locked ICC bytes above;
- all final raster image objects CMYK;
- vector text and vector QR;
- no unexpected font substitution;
- no raster recompression that reduces approved source quality;
- page-box validation;
- Poppler render validation;
- Ghostscript render validation;
- QR scan validation from the rendered PDF;
- print preflight must hard-fail on unexpected RGB raster objects, wrong page boxes, missing output intent, missing assets, invalid fonts, or unreadable QR.

## 6. QR and logo decisions - product-owned, not printer blockers

- Use exactly one QR code.
- Place it on Interior Spread 11, the credits / closing spread.
- Remove the QR and `Continue Adventure` chip from Story Spread 8.
- For v0, the configurable destination is `https://beki.ge`.
- Never bake the QR into an illustration.
- No final official BEKI wordmark is approved for production yet.
- Use only the exact approved transparent Beki PNG on the credits spread.
- Do not use a legacy opaque Beki raster or a draft logo.
- Keep the back cover environment-only, without a Beki character or draft wordmark.

## 7. What may still require the printer later

No additional printer input is required to generate the current fixed v0 press files.

Printer input becomes necessary only when:

- the binding or physical cover construction changes;
- the final page count or paper stock changes the spine geometry;
- another printer or print process is selected;
- the printer requests a different ICC profile or PDF preset;
- the physical proof reveals a production issue.

The physical proof and final mass-production approval remain production gates, but they are not blockers for the current implementation or PDF generation.
