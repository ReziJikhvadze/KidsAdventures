# 01 - Current State

## What is complete

- Approved Admin information architecture.
- Responsive navigation and shared visual system.
- Overview, Orders, Order Detail, Production, Fulfillment, Customers,
  Promotions, Audit, Settings, Partner, and Login routes.
- Search and filtering interactions.
- Mock PDF approval with immutable snapshot semantics.
- Mock Print Partner handoff and sequential print statuses.
- Mock courier creation with an in-memory/localStorage duplicate guard.
- Mock append-only Audit Event UI.
- Desktop and mobile UI behavior.

## What is mocked

| Concern | Current location | Production replacement |
| --- | --- | --- |
| Orders | `app/data.ts` | `GET /admin/orders` |
| Overview metrics | Derived from `app/data.ts` | `GET /admin/overview` |
| Print jobs | `AdminState.tsx` | Database + Print API |
| PDF approval | `AdminState.tsx` | Transactional backend mutation |
| Courier creation | `AdminState.tsx` + `setTimeout` | Courier adapter + idempotency |
| Audit log | `AdminState.tsx` | Append-only Audit table/event stream |
| Customers | Page-local mock arrays | Customer API |
| Promotions | Page-local React state | Promotion API |
| Authentication | Prototype UI | Production identity provider + session |
| Persistence | `localStorage` | Production database |
| Files | Display-only metadata | Private object storage |

## Explicit warning

The browser guards in `AdminState.tsx` demonstrate intended UX only. They are
not security controls. Every invariant must be reimplemented and enforced on
the server inside database transactions.

## Approved UX that should not regress

- Full parent emails and phone numbers are visible to authorized Admin roles.
- Print Partner receives only production-required data.
- The exact approved PDF version and hash remain visible through fulfillment.
- Courier creation remains disabled before `PACKED`.
- A second courier creation returns the existing shipment.
- Every important action appears in Audit Log.
- Date filters remain available in operational list/reporting views.
- Problems are visible through Attention Queue, status badges, and filters.

