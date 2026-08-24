# PDF fonts (SIL Open Font License)

- **Adventrya Sans** — body text, backed by Noto Sans Georgian (regular, semi-bold, bold)
- **Adventrya Serif** — display headings and titles, backed by Noto Serif Georgian (semi-bold)
- **Adventrya Display** — the cover title only, backed by Ottia (Wicked Letters, via Future Fonts)

Ottia carries all 33 modern Georgian letters, Latin and digits, but **not** the dash, colon,
semicolon, ellipsis or apostrophe. Every call that names it passes the Noto families behind it,
so QuestPDF borrows the missing characters per glyph instead of printing a box. Skia's own
system-font fallback hides this on a developer's Windows machine; the Linux container has no
such safety net, which is why the chain is explicit.

**The bundled file is the v0.1 trial** (`Ottia-v01-Trial-Regular.*`), licensed for evaluation
only. A book that is sold must ship the purchased file — and the licence counts eBook copies
(500 at the $30 tier), so the count is worth watching as orders grow.

Bundled for QuestPDF rendering. The family names match the web brand aliases so a
printed book and the on-screen reader look like the same product. Source:
[Google Fonts / Noto](https://fonts.google.com/noto).
