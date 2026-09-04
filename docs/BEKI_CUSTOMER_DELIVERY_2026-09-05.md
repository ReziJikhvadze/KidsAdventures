# Customer delivery and manufacturing readiness

Product-owner ruling, 2026-09-05: print preparation failures must not prevent a family from reading and downloading an otherwise valid completed book. This overrides earlier requirements that required print approval before customer publication.

The format remains one canonical PDF: cover wrap + 11 interior spreads, including exactly 8 story spreads, continuation QR on story spread 8, and credits left / approved pattern right on the final spread. Reader and customer download serve the same bytes.

## Release behavior

- Missing, failed or timed-out upscaling retains the verified original composited artwork. It does not claim extra detail or 300-PPI print approval.
- Failed print conversion/preflight retains the original composed PDF for customer validation.
- Customer validation still requires a readable 12-page canonical structure, all rendered pages, embedded fonts and the correct scanned QR. When printing is already withheld, Poppler can validate customer delivery even if Ghostscript is unavailable. The renderer failure remains in the diagnostic report.
- A customer PDF passing its own gates allows `Completed` and `PdfUrl`. `PrintPdfUrl` stays null until raw manufacturing gates pass; a policy waiver cannot approve manufacturing.
- `press-status.json`, print preflight reports, release gates, and a `PRINT_PREPARATION_HELD` admin blocker preserve the cause. Admin shows customer-ready / printing-held separately.
- Print downloads never substitute the customer PDF. Print/shipping transitions reject a missing approved print PDF. Rebuilds revoke stale print permission before overwriting canonical bytes.

## Recover an already-failed book without generation charges

In Admin > Orders, open a canonical book with a `PRINT_PREFLIGHT_FAILED` error and choose **PDF-ის აღდგენა — ახალი ხატვის გარეშე**.

Equivalent authenticated admin request: `POST /api/admin/books/{bookId}/recover-customer-pdf`.

Recovery requires a paid, owner-matched preview plan, the current stored artwork contract, all 8 spreads, the stored cover master and composition receipts. It verifies approved assets and source hashes, composes the PDF, runs customer validation, and publishes only if customer gates allow it. It never calls story/image generation or an upscaler. Missing or incompatible artwork is reported; it is not silently redrawn. Concurrent recovery attempts are guarded by a status compare-and-set. Already completed books are left unchanged.

Printing remains held after customer recovery. Operators must repair the print dependency/artifacts and validate manufacturing separately. Do not use the redraw action just to retry PDF assembly: redraw is a different operation that can spend money.

## Verification

Offline regressions cover customer publication with print failures, stale print permission revocation, explicit print-download refusal, manufacturing/shipping holds, corrupt PDF rejection, print timeout isolation, and customer rendering without Ghostscript.

The local failed test book was recovered from stored artwork, visually reviewed across all 12 pages, and checked through customer and admin endpoints: customer book/download 200, admin reading PDF 200, admin print PDF 409. Customer and admin reading downloads had identical SHA-256 hashes. No live-provider generation was used for recovery or testing.
