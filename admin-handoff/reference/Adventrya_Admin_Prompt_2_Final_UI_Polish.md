# Adventrya Admin — Prompt 2: Final UI Polish & Responsive Operations Console

You are editing the latest existing **Adventrya Admin UX** prototype.

Prompt 1 is approved. Its information architecture, routes, operational state model, navigation, tables, filters, statuses, immutable PDF approval, Print Partner handoff, courier idempotency, privacy boundaries, and Audit Log behavior are the product contract.

Your task is to deliver the final visual-polish layer without redesigning or simplifying the approved UX.

This is not a new dashboard. Do not rebuild it from scratch.

---

## 1. Non-negotiable working principle

Before editing:

1. Inspect the existing shared shell, visual tokens, components, routes, responsive behavior, and mock state.
2. Identify repeated components and polish them centrally.
3. Preserve the current route structure and every approved interaction.
4. Modify the current implementation surgically.
5. Do not replace the existing operational model with generic dashboard templates.
6. Do not remove information merely to make the interface look cleaner.
7. Do not introduce decorative visuals that compete with operational clarity.
8. Do not change existing pricing, statuses, workflow rules, role permissions, privacy rules, PDF approval rules, or courier rules.

The final result must feel intentionally designed for Adventrya, not like a reskinned SaaS template.

---

## 2. Product character

Adventrya’s customer-facing product is emotional, magical, story-driven, and premium.

The Admin must inherit the brand in a restrained operational form:

- calm;
- precise;
- premium;
- warm;
- trustworthy;
- visually distinctive;
- fast to scan;
- built for daily use.

Use a **Quiet Magical Operations Console** direction.

The interface must not feel:

- childish;
- game-like;
- overly decorative;
- neon;
- cyberpunk;
- corporate-blue;
- sterile;
- like a generic Bootstrap admin;
- like a collection of unrelated cards.

Magic should appear through refined details: deep midnight tones, warm parchment surfaces, subtle gold accents, carefully controlled shadows, elegant typography, and a restrained star or story-path motif.

---

## 3. Visual system

### Core palette

Use a restrained Adventrya admin palette:

- App canvas: warm mist / parchment-tinted gray
- Primary surface: warm white
- Raised surface: pure white
- Sidebar: deep midnight indigo
- Sidebar secondary surface: muted violet-indigo
- Primary text: near-black midnight ink
- Secondary text: calm slate
- Brand accent: antique storybook gold
- Brand accent hover: warmer amber gold
- Success: refined forest green
- Warning: warm amber
- Danger: muted ruby
- Information: calm blue
- Continuation / special workflow: restrained violet

Rules:

- Gold is a directional accent, not a background color for large areas.
- Operational status colors must remain semantically consistent.
- Avoid low-contrast gray text.
- Do not communicate status using color alone.
- All critical text and controls must meet accessible contrast.

### Typography

Use the existing high-quality sans-serif system and improve the hierarchy.

- Page title: 28–32px desktop, 24–27px mobile
- Section title: 16–18px
- Primary body/table text: 12–14px
- Secondary metadata: never smaller than 10px
- Labels: 10–12px with restrained letter spacing
- Numeric KPIs: 28–34px, tabular figures where useful

Georgian text must be fully legible. Do not compress it into overly small labels.

### Spacing and geometry

- Use an 8px-based spacing rhythm.
- Prefer 12–16px radii for major surfaces.
- Prefer 8–10px radii for compact controls.
- Use borders and subtle layered shadows together.
- Avoid excessive empty space in operational tables.
- Avoid dense clusters without clear grouping.
- Keep primary content within a controlled max-width while allowing wide operational tables to use available desktop space.

---

## 4. Shared Admin Shell

Polish the shared shell centrally.

### Sidebar

Keep the approved navigation groups and route order.

Improve:

- brand lockup;
- spacing;
- icon alignment;
- selected-state hierarchy;
- hover and keyboard-focus states;
- notification counts;
- Print Partner view entry;
- current-user block.

Required behavior:

- Selected navigation item must be unmistakable.
- Use a restrained gold story-path indicator for the active route.
- The sidebar must feel premium but quiet.
- Notification badges must remain compact.
- Long Georgian labels must never clip.
- On mobile, preserve the current slide-over navigation and backdrop.
- The mobile menu must be comfortable to tap and dismiss.

### Top bar

Improve:

- global search;
- keyboard shortcut indicator;
- notification action;
- language switcher;
- spacing and visual balance.

Search must feel like a primary operational tool, not an unstyled text input.

### Page heading

Create a strong but compact page-header system:

- clear title;
- concise supporting copy;
- date or operational context when present;
- primary and secondary actions aligned consistently;
- mobile actions stack without becoming oversized.

---

## 5. Buttons, controls, and interaction states

Create one coherent hierarchy:

- Primary action
- Secondary action
- Quiet/tertiary action
- Destructive action
- Icon-only action

Required states:

- default;
- hover;
- pressed;
- keyboard focus;
- disabled;
- loading;
- success where relevant.

Rules:

- Do not use black rectangles for every primary action.
- Use Adventrya midnight for primary operational actions and gold only for especially important handoffs.
- Icon-only actions require accessible labels and tooltips or clear surrounding context.
- Minimum touch target on mobile: 44×44px.
- Forms must have clear labels, focus rings, helper text, and error states.
- Selects, search fields, date controls, and segmented controls must share one visual language.

---

## 6. Data tables

Tables are the core of the product. Polish them with exceptional attention.

Required:

- stronger header hierarchy;
- readable 12–13px row text;
- clear row hover;
- comfortable but efficient row height;
- sticky table headers when the table scrolls inside a long workspace;
- aligned numbers;
- tabular figures for IDs, prices, counts, hashes, versions, and dates;
- clear primary/secondary cell hierarchy;
- consistent action column;
- visible selected/focused row state;
- subtle horizontal-scroll affordance;
- graceful empty states;
- preserved mobile horizontal scrolling.

Do not:

- make table text tiny;
- create zebra-striping with high contrast;
- center all values;
- hide necessary columns;
- truncate critical IDs without a way to read them;
- rely on icon-only status.

Contact information in the full Admin remains visible.

The Print Partner role must continue receiving the restricted DTO only.

---

## 7. Status system

Create one consistent status badge system across:

- payment;
- generation;
- print;
- delivery;
- courier;
- SLA;
- promotion;
- integration;
- audit severity.

Every badge must use:

- readable label;
- semantic color;
- subtle border;
- optional dot or icon;
- consistent height and padding.

Critical statuses must visually surface without making the entire interface alarming.

At-risk and breached states must be distinguishable.

---

## 8. Filters and date controls

Preserve all functional filters added in Prompt 1.

Polish:

- date-range bar;
- preset buttons;
- advanced-filter panel;
- filter count;
- quick-filter chips;
- reset action;
- filtered-result summary.

Required:

- active filters are immediately visible;
- controls align on desktop;
- filters stack logically on mobile;
- date fields remain usable without overflow;
- quick filters are horizontally scrollable on small screens;
- empty filtered states clearly explain what happened and offer reset.

---

## 9. Overview

Create a calm executive operations overview.

### KPI cards

Improve:

- numeric hierarchy;
- trend indicators;
- comparison labels;
- spacing;
- visual grouping.

KPI cards must not look like four identical empty boxes.

Use subtle differentiation through icon containers, top accents, micro-bars, or restrained background tones—not decorative illustrations.

### Action queue

The “Needs attention” area is more important than decorative analytics.

Make each row:

- easy to scan;
- visibly actionable;
- semantically colored;
- keyboard accessible.

### Fulfillment health

Keep the SLA visualization restrained and legible.

### Recent orders and exceptions

Prioritize operational next action, current owner, issue, and status legibility.

---

## 10. Orders

The Orders workspace must feel like the operational command center.

Polish:

- toolbar;
- global/local search distinction;
- quick-filter row;
- advanced-filter panel;
- result summary;
- CSV export;
- wide data table;
- order actions;
- issue and owner cells.

Critical and at-risk orders must be discoverable in seconds.

Do not visually over-emphasize healthy rows.

---

## 11. Order Detail

The Order Detail must clearly answer:

1. What was purchased?
2. Who is the order for?
3. What is its current operational state?
4. Is there a problem?
5. Who owns the next step?
6. Which exact PDF is approved?
7. Was it sent to the partner?
8. Can a courier order safely be created?
9. What happened previously?

Polish:

- status rail;
- tab navigation;
- information hierarchy;
- book and character cards;
- book spread preview;
- payment summary;
- fulfillment steps;
- immutable PDF snapshot;
- courier panel;
- activity timeline.

The immutable print snapshot must be one of the most trustworthy surfaces in the product.

Highlight:

- file name;
- File ID;
- version;
- SHA-256 hash;
- preflight state;
- approver;
- approval time;
- partner receipt.

Do not make the approval UI look like an ordinary upload card.

Courier creation must visually communicate the idempotency lock and existing external order when present.

---

## 12. Book Production

Make the production queue feel active, controlled, and safe.

Polish:

- queue summary;
- generation/review table;
- failed and review-required states;
- refresh feedback;
- generation-cost insights;
- regeneration safety notice.

Failed generation and required human review must have stronger hierarchy than successful work.

---

## 13. Print & Delivery

Preserve both:

- Pipeline view
- List view

### Pipeline

Improve the seven-stage workflow without turning it into a Trello clone.

Required:

- compact columns;
- clear stage counts;
- readable cards;
- visible owner;
- due date;
- SLA;
- immutable file state;
- tracking state;
- next action.

Horizontal movement must feel intentional on smaller desktops.

### List

Use the same status hierarchy and interaction language as Orders.

### Handoff conveyor

The Admin → Print Partner → Pickup → Courier path must remain obvious.

Use a refined process rail rather than four unrelated cards.

### Courier operations

Clearly separate:

- not eligible;
- ready to create;
- creation in progress;
- already created;
- failed;
- delivered.

The UI must make duplicate creation feel impossible.

---

## 14. Customers

Create a useful parent-account workspace.

Polish:

- customer list;
- selected-customer state;
- full contact information;
- child profiles;
- owned books;
- Digital-to-Print action;
- internal note composer;
- order history.

The selected profile must remain visually anchored while browsing related information.

Do not expose customer information in the Print Partner role.

---

## 15. Promotions

Polish:

- promotion metrics;
- searchable table;
- status filter;
- active/expired state;
- usage progress;
- create-promotion modal;
- 100% discount warning.

The modal must feel safe and deliberate.

Do not advertise internal QA promocodes.

---

## 16. Audit Log

The Audit Log must communicate immutability and traceability.

Polish:

- integrity banner;
- date and severity filters;
- search;
- category and actor hierarchy;
- immutable indicator;
- related-order link;
- export action;
- event detail readability.

Important destructive, approval, handoff, and courier actions must be easy to isolate.

---

## 17. Settings

Keep settings operational and permission-aware.

Polish:

- role cards;
- permission matrix;
- integration adapters;
- SLA settings;
- immutable safety rules;
- partner-routing flow.

Dangerous changes need clear confirmation or explanatory feedback.

---

## 18. Print Partner workspace

The Print Partner workspace must look like a focused extension of Adventrya—not a duplicate of the full Admin.

Preserve privacy:

Do not expose:

- customer email;
- customer phone;
- delivery address;
- payment details;
- unrelated customer history;
- internal notes;
- admin-only audit information.

Show only:

- Print Job ID;
- Order reference;
- book title;
- approved print file;
- File ID;
- version;
- SHA-256 hash;
- preflight result;
- print specification;
- quantity;
- due date;
- production status;
- pickup handoff state;
- partner production notes.

Polish:

- restricted-role header;
- inbox alert;
- queue KPIs;
- job list;
- selected job detail;
- approved-file card;
- status transition;
- print manifest download;
- pickup label.

The exact approved file must remain unmistakable.

---

## 19. Motion

Use motion sparingly and functionally:

- sidebar active indicator;
- panel hover;
- filter expansion;
- modal entrance;
- toast entrance;
- status transition;
- loading state;
- selected-row transition;
- success confirmation.

Timing:

- micro-interactions: 120–180ms;
- panels/modals: 180–240ms;
- use ease-out curves.

Do not use:

- bouncing;
- floating particles;
- constant pulsing;
- parallax;
- child-facing magical effects;
- motion that delays work.

Respect `prefers-reduced-motion`.

---

## 20. Responsive behavior

Desktop is the primary working environment, but mobile and tablet must remain fully usable.

Test at:

- 1366×768
- 1440×900
- 1920×1080
- 1024×768
- 768×1024
- 430×932
- 390×844

Required:

- no page-level horizontal overflow;
- wide tables scroll only inside their containers;
- primary actions remain visible;
- filters wrap or stack logically;
- side navigation becomes an accessible drawer;
- tap targets remain large enough;
- page titles and supporting copy do not collide with actions;
- modals fit within the viewport;
- partner job detail stacks correctly;
- Order Detail tabs remain reachable;
- Kanban remains navigable;
- content does not become unreadably small.

---

## 21. Accessibility

Required:

- visible keyboard focus;
- semantic headings;
- meaningful labels;
- `aria-live` for dynamic feedback;
- accessible dialogs;
- disabled-state clarity;
- no color-only meaning;
- adequate contrast;
- reduced-motion support;
- no inaccessible icon-only controls.

---

## 22. Non-regression contract

Do not damage or remove:

- all approved routes;
- navigation order;
- date filters;
- full email and phone visibility in full Admin;
- order search;
- issue, status, and owner filters;
- CSV export;
- Pipeline/List switcher;
- immutable PDF File ID/version/hash;
- preflight requirement;
- approval lock;
- partner handoff receipt;
- Print Partner restricted DTO;
- Print Partner manifest;
- courier Packed eligibility;
- courier idempotency key;
- duplicate-order lock;
- saved external courier order;
- complete Audit Log;
- internal customer notes;
- Promotion interactions;
- mock data and operational states;
- mobile navigation;
- existing frontend-only architecture.

---

## 23. Final QA

Do not mark complete until:

1. Lint passes.
2. Production build passes.
3. Every route opens.
4. No route has page-level horizontal overflow.
5. Main interactions work with mouse and keyboard.
6. Filters, search, view switchers, modals, toasts, and exports work.
7. The approved PDF cannot be silently replaced.
8. The Print Partner cannot see restricted personal data.
9. A duplicate courier order cannot be created.
10. Critical actions remain visible in Audit Log.
11. Georgian text remains readable at all tested widths.
12. Desktop and mobile screenshots show consistent visual quality.

Deliver the updated working ChatGPT Site and a short route-by-route QA summary.

