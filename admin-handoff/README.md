# Adventrya Admin - Developer Handoff

This package contains the approved Adventrya Admin frontend prototype plus the
technical contract required to connect it to the production platform.

## Important: current implementation status

The current application is a **frontend-only clickable prototype**.

- Orders and dashboard data are mocked in `app/data.ts`.
- Print jobs, PDF approvals, courier creation, and audit events are held in
  React state and persisted to browser `localStorage` in
  `app/components/AdminState.tsx`.
- `db/schema.ts` is intentionally empty.
- No production API, database, payment provider, generation service, print
  partner service, courier provider, email/SMS service, or production RBAC is
  connected.
- The existing UI, routes, status language, safety constraints, and responsive
  behavior are approved and should be preserved.

Do not ship the current mock state as production logic.

## Start locally

Requirements:

- Node.js `>=22.13.0`
- npm

```bash
npm ci
npm run dev
```

Then open the local URL printed by Vite.

Quality checks:

```bash
npm run lint
npm run build
```

## Primary routes

| Route | Purpose |
| --- | --- |
| `/` | Operations overview and attention queue |
| `/orders` | Searchable and filterable order list |
| `/orders/:id` | Complete order detail and operational controls |
| `/production` | Story/book generation queue |
| `/fulfillment` | Print and delivery pipeline |
| `/customers` | Parent profiles and purchase history |
| `/promotions` | Promocode management |
| `/audit` | Append-only operational audit trail |
| `/settings` | Roles, permissions, SLA, and integration state |
| `/partner` | Restricted Print Partner workspace |
| `/login` | Prototype authentication screen |

## Recommended reading order

1. `docs/01_CURRENT_STATE.md`
2. `docs/02_ARCHITECTURE.md`
3. `docs/03_ROUTE_AND_INTEGRATION_MAP.md`
4. `docs/04_DATA_MODEL.md`
5. `docs/05_STATE_MACHINES.md`
6. `docs/06_API_CONTRACT.md`
7. `docs/openapi.yaml`
8. `docs/07_AUTH_RBAC.md`
9. `docs/08_PRINT_AND_COURIER.md`
10. `docs/09_SECURITY_PRIVACY.md`
11. `docs/10_IMPLEMENTATION_PLAN.md`
12. `docs/11_QA_ACCEPTANCE.md`
13. `docs/12_OPEN_DECISIONS.md`

The visual product references are under `reference/`.

## Production boundary

Recommended domains:

- Customer platform: `https://adventrya.com`
- Admin: `https://admin.adventrya.com`
- Shared API: `https://api.adventrya.com`

The Admin must communicate with the backend API. It must not connect directly to
the production database, payment provider, generation service, print partner,
or courier provider from the browser.

## Non-negotiable safety invariants

1. Only an approved print asset can create a Print Job.
2. Approval locks the exact file ID, version, SHA-256 hash, approver, and time.
3. A Print Partner can access only jobs assigned to that partner.
4. A Print Partner cannot access parent contact data, payment data, child
   photos, interests, or generation prompts.
5. Courier creation is allowed only after `PACKED`.
6. Courier creation must be idempotent and must never create a duplicate
   shipment.
7. Every privileged action creates an append-only Audit Event.
8. Previously purchased books and approved print snapshots are immutable.
9. A Digital-to-Print upgrade prints the existing book and never regenerates
   the story.

## Handoff acceptance

The handoff is complete only when the developer can:

- run the prototype locally;
- explain which files contain mock state;
- map each screen to the proposed API;
- implement the state transitions without bypassing safety guards;
- demonstrate role restrictions;
- pass the scenarios in `docs/11_QA_ACCEPTANCE.md`.

