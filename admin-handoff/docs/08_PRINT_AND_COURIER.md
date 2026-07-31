# 08 - Print Partner and Courier Integration

## Print asset preparation

Before approval, run deterministic preflight:

- expected cover + 7 story pages;
- correct dimensions and bleed;
- required color profile;
- embedded fonts;
- minimum image resolution;
- QR presence and destination verification;
- no missing/corrupt pages;
- file SHA-256 calculation.

Store the result against the exact Print Asset version.

## Approval transaction

1. Lock the target order/Print Job row.
2. Verify actor has `print_assets.approve`.
3. Verify preflight and QR checks passed.
4. Verify no active approved version already exists.
5. Create immutable Print Approval snapshot.
6. Create or update Print Job to `SENT`.
7. Write Audit Event/outbox event.
8. Commit.
9. Notify the assigned Print Partner asynchronously.

If another version was already approved, return `409` and require an explicit
revision/supersede flow.

## Partner Inbox

Recommended implementation: restricted role inside the same Admin application.

Partner response DTO may contain:

- Print Job ID;
- public Order reference;
- book title or production-safe label;
- quantity;
- format/technical specification;
- due date;
- approved file version and hash;
- signed download URL;
- production status;
- production notes.

It must not contain:

- parent email or phone;
- delivery address;
- payment or promotion details;
- child photo;
- child interests;
- generation prompt;
- internal margin/cost data;
- jobs assigned to another partner.

## Partner status API

Partner sends only the requested next status. The backend validates the current
status and allowed transition. Each accepted transition writes an Audit Event.

## Courier adapter

Use a provider-neutral interface:

```ts
interface CourierAdapter {
  createShipment(input: CreateShipmentInput, idempotencyKey: string):
    Promise<CreateShipmentResult>;
  getShipment(externalOrderId: string): Promise<ShipmentStatus>;
  cancelShipment(externalOrderId: string): Promise<void>;
  verifyWebhook(headers: Headers, rawBody: Uint8Array): VerifiedCourierEvent;
}
```

## Courier creation transaction

1. Lock Print Job.
2. Require `PACKED`.
3. Search existing non-cancelled shipment.
4. If found, return existing shipment.
5. Reserve unique idempotency key.
6. Persist `CREATING`.
7. Commit and call provider.
8. Persist provider IDs and `CREATED`, or safe `FAILED`.
9. Write Audit Event.

Unknown provider response/time-out:

- do not create a new key;
- query provider using the same idempotency/reference;
- reconcile before retrying.

## Courier PII

Courier receives only:

- recipient name;
- phone;
- delivery address;
- delivery note;
- package dimensions/weight;
- pickup location.

Do not send child profile, book story, payment data, or generation data.

