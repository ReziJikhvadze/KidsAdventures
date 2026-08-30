# PDF fonts

- **Adventrya Sans** — body text, backed by Noto Sans Georgian (regular, semi-bold, bold)
- **Adventrya Serif** — the **A5 book's** display headings, backed by Noto Serif Georgian (semi-bold)
- **Adventrya Display** — the cover title only, backed by Ottia (Wicked Letters, via Future Fonts)

## The Beki book's whitelist

The Beki interior may be set in **Noto Sans Georgian Regular and Bold and nothing else**
(handoff §6 Step 8), with the licensed Ottia on the cover title. A shipped PDF was found
embedding `NotoSerifGeorgian-SemiBold` and QuestPDF's own default `Lato-Regular`; both are
now unreachable from that composer — Noto Serif stays for the A5 book, and every Beki page
names the body face as its default text style so nothing can fall through to Lato.

Four of these files are **approved production assets**, listed with their SHA-256 in
`Assets/BekiComposite/layout/beki_layout_asset_registry_v1.json` and verified before every
Beki book is composed. They came from `BEKI_Developer_Production_Assets_v1/fonts` and their
hashes match that pack's `APPROVED_ASSET_MANIFEST_v1.csv`:

| File | Manifest asset id | Role |
|---|---|---|
| `NotoSansGeorgian-Regular.ttf` | `noto_sans_georgian_regular` | Interior body |
| `NotoSansGeorgian-Bold.ttf` | `noto_sans_georgian_bold` | Interior emphasis |
| `Ottia-v01-Regular.ttf` | `ottia_regular_ttf` | Cover title |
| `Ottia-v01-Regular.otf` | `ottia_regular_otf` | Cover title, alternate outline format |

## Ottia

`Ottia-v01-Regular.*` is the **licensed** v0.1 build supplied in the production pack. The
evaluation-only trial (`Ottia-v01-Trial-Regular.*`) that used to be bundled here has been
removed, and the registry now refuses to compose a book if it reappears — it was licensed for
evaluation only and it reached a sold book.

Ottia carries all 33 modern Georgian letters, Latin and digits, but **not** the dash, colon,
semicolon, ellipsis or apostrophe. Every call that names it passes Noto Sans Georgian behind
it, so QuestPDF borrows the missing characters per glyph instead of printing a box. Skia's own
system-font fallback hides this on a developer's Windows machine; the Linux container has no
such safety net, which is why the chain is explicit.

**Still open (owner):** the licence's *embedding* scope for print. The handoff's checklist lists
"Ottia licence scope (embed vs outlines)" as blocking the final cover PDF, and the licence counts
eBook copies, so the count is worth watching as orders grow. Nothing here decides that; the cover
print artifact stays withheld until it is decided.

The remaining faces (Noto Serif Georgian, Nunito, Fredoka) are the A5 book's and are SIL Open
Font License / Google Fonts downloads. Source: [Google Fonts / Noto](https://fonts.google.com/noto).
The family names match the web brand aliases so a printed book and the on-screen reader look
like the same product.
