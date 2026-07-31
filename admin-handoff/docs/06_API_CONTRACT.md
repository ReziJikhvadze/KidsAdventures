# 06 - API Contract

`docs/openapi.yaml` contains the machine-readable starter contract. This file
defines behavior that is not fully expressed by endpoint shapes.

## General rules

- Base path: `/v1`
- JSON request and response bodies.
- UTC ISO-8601 timestamps.
- Money returned as `{ amountMinor, currency }`.
- Cursor pagination for operational lists.
- All list endpoints support `from`, `to`, and relevant status filters.
- Every response includes or propagates a request/correlation ID.
- Privileged mutations require an authenticated user and permission.

## Error format

```json
{
  "error": {
    "code": "INVALID_STATE_TRANSITION",
    "message": "Courier shipment requires a PACKED print job.",
    "details": {
      "currentStatus": "PRINTING",
      "requiredStatus": "PACKED"
    },
    "requestId": "req_..."
  }
}
```

## Idempotent mutations

Require an `Idempotency-Key` header for:

- payment/order activation handlers;
- print approval submission;
- Print Job creation;
- courier shipment creation;
- webhook event processing.

The server stores:

- key;
- actor/scope;
- request hash;
- response status/body;
- created/expiry timestamps.

Reusing a key with a different request body returns `409`.

## Concurrency

Use a transaction and row-level lock or optimistic `version` field for status
mutations. The server must re-read current state before changing it.

## File access

The API returns short-lived signed URLs only after authorization. Never store a
permanent public URL for:

- child photos;
- preview images;
- digital books;
- printable PDFs;
- source generation assets.

## Webhooks

Verify:

- provider signature;
- timestamp tolerance;
- unique provider event ID;
- payload schema;
- allowed state transition.

Respond quickly, persist the event, and process heavy work asynchronously.

## Audit behavior

The same transaction that performs a privileged mutation writes the
corresponding Audit Event or an outbox record that guarantees the Audit Event.
UI-only logging is insufficient.

