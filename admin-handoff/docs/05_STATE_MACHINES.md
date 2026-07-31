# 05 - State Machines

Reject invalid transitions with `409 Conflict`. Do not silently skip states.

## Order

```text
DRAFT
  -> PAYMENT_PENDING
  -> PAID
  -> ACTIVE
  -> COMPLETED

PAYMENT_PENDING -> PAYMENT_FAILED
ACTIVE -> CANCEL_REQUESTED -> CANCELLED
PAID/ACTIVE -> REFUND_REQUESTED -> REFUNDED
```

## Generation

```text
QUEUED
  -> GENERATING
  -> REVIEW
  -> READY

GENERATING -> FAILED
FAILED -> RETRY_QUEUED -> GENERATING
```

Retry rules:

- bounded automatic retry;
- manual retry permission;
- every attempt is a separate record;
- completed pages are not overwritten without versioning.

## Print

```text
ASSET_GENERATING
  -> READY_FOR_REVIEW
  -> APPROVED
  -> SENT
  -> ACCEPTED
  -> PRINTING
  -> QUALITY_CHECK
  -> PACKED
  -> READY_FOR_PICKUP
  -> COMPLETED
```

Rules:

- `APPROVED` requires successful preflight and QR verification.
- Approval locks file ID, version, and SHA-256.
- `SENT` routes the approved snapshot to the assigned partner.
- Partner may transition only:
  - `SENT -> ACCEPTED`
  - `ACCEPTED -> PRINTING`
  - `PRINTING -> QUALITY_CHECK`
  - `QUALITY_CHECK -> PACKED`
- Partner cannot change the approved file.
- Rejection/damage/reprint must be explicit exception states, not backward
  transitions.

## Courier

```text
NOT_CREATED
  -> CREATING
  -> CREATED
  -> READY_FOR_PICKUP
  -> PICKED_UP
  -> IN_TRANSIT
  -> DELIVERED

CREATING -> FAILED
CREATED/IN_TRANSIT -> EXCEPTION
EXCEPTION -> IN_TRANSIT | RETURNED | CANCELLED
```

Rules:

- Create only when Print Job is `PACKED`.
- Create with `Idempotency-Key: courier:{print_job_id}`.
- Retry with the same key.
- If a shipment exists, return it instead of creating another one.
- Webhook updates must be idempotent and ordered by provider event time/version.

## Digital-to-Print upgrade

```text
DIGITAL_OWNED
  -> UPGRADE_PAYMENT_PENDING
  -> PRINT_UPGRADE_PAID
  -> READY_FOR_REVIEW
```

The existing generated book is reused. Story and pages are never regenerated.

