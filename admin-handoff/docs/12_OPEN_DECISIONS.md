# 12 - Open Decisions

These are intentionally not guessed in the handoff. Confirm them before
production implementation.

| Decision | Why it matters | Recommended default |
| --- | --- | --- |
| Backend runtime | API framework and hosting | Match the main Adventrya platform |
| Primary database | Transactions and constraints | PostgreSQL-compatible relational DB |
| Authentication | Admin and partner identity | Managed OIDC with MFA support |
| Admin hosting | Domain, cookies, deployment | `admin.adventrya.com` |
| API hosting | Shared backend boundary | `api.adventrya.com` or same-origin gateway |
| Object storage | Photos, books, print PDFs | Private S3-compatible bucket |
| Job queue | Generation and notifications | Managed queue with retries/DLQ |
| Payment provider | Webhook and refund API | Confirm Georgian checkout provider |
| Courier provider | Shipment API/webhooks | Confirm provider before adapter build |
| Print Partner transfer | Same Admin vs external portal | Restricted role in same Admin |
| Email/SMS providers | Notifications and OTP | Provider adapters |
| Monitoring | Error and operational alerts | Central logs + error monitoring |
| Retention policy | Child data and legal needs | Approve with legal/accounting input |
| Partner SLA | Alerts/escalation | Admin review 2h; partner ACK 4h as current prototype |

## Product decisions already fixed

- Digital: 14 GEL.
- Printed + Digital: 79 GEL including delivery in Georgia.
- Digital-to-Print upgrade: 65 GEL.
- Tbilisi delivery estimate: 4-5 days.
- Other Georgia delivery estimate: 5-8 days.
- Book: cover + 7 illustrated pages.
- Book language is per book.
- Previously purchased books are immutable.
- Print Upgrade does not regenerate the book.

