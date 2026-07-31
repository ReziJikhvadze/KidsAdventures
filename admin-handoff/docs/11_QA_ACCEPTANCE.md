# 11 - QA Acceptance Scenarios

## Authentication and roles

1. Anonymous user cannot access Admin data.
2. Support can view an order but cannot approve a PDF.
3. Operations can create courier shipment only with permission.
4. Print Partner sees only assigned jobs.
5. Print Partner API responses contain no prohibited PII.
6. Removed role/session loses access promptly.

## Orders and filters

1. Search by Order ID, parent email, phone, child, and title.
2. Filter every operational list by date.
3. Combine date, product, status, and owner filters.
4. Pagination does not duplicate or skip records.
5. Problem order is discoverable from Overview and Orders.

## Generation

1. Paid order creates one Generation Job.
2. Duplicate payment webhook does not create another job.
3. Failed generation records attempt and error.
4. Manual retry creates a new attempt, not an overwritten record.
5. Ready book remains immutable after child-profile edits.

## Print approval

1. Failed preflight cannot be approved.
2. Wrong/old asset version cannot be sent silently.
3. Approval stores file ID, version, SHA-256, approver, and time.
4. Approved snapshot cannot be edited.
5. Partner downloads the exact approved hash.
6. Approval and handoff create Audit Events.

## Print Partner

1. Partner A cannot access Partner B job by changing URL/ID.
2. Only the next allowed status is accepted.
3. Backward/skipped transition returns `409`.
4. `PACKED` returns responsibility to Admin.
5. Every partner status writes actor and timestamp.

## Courier

1. Button/API is blocked before `PACKED`.
2. First request creates one shipment.
3. Double click returns processing/existing shipment.
4. Network timeout and retry use the same idempotency key.
5. Duplicate webhook does not duplicate events or regress status.
6. Provider exception appears in Attention Queue.
7. Courier receives only delivery-required PII.

## Promotions

1. Invalid/inactive/expired code is rejected.
2. Partial discount is calculated in integer minor units.
3. 100% code produces zero total and still activates the order.
4. Usage limit is enforced under concurrent requests.
5. Print Upgrade promotion calculates from 65 GEL.

## Audit

1. Privileged actions create Audit Events.
2. Application has no update/delete Audit endpoint.
3. Actor, role, target, time, request ID, and result are present.
4. Audit filters and export respect permissions.

## Responsive and UI regression

Test at minimum:

- 1366x768
- 1440x900
- 1920x1080
- 390x844
- 430x932

Verify:

- no hidden primary action;
- no clipped table controls;
- no mobile horizontal page overflow;
- internal table scrolling where necessary;
- keyboard focus is visible;
- status is not communicated by color alone.

