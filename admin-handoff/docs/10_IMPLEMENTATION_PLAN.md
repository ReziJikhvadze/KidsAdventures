# 10 - Implementation Plan

## Phase 0 - Decisions and setup

- Confirm backend/database/hosting.
- Confirm identity provider.
- Confirm payment, generation, courier, email, and SMS providers.
- Set up Local, Staging, and Production environments.
- Configure CI, lint, test, build, migration, and secret management.

Exit: empty production architecture is deployable and authenticated.

## Phase 1 - Core read model

- Implement identity and RBAC.
- Implement core database schema.
- Import/create seed data in Staging.
- Add typed API client.
- Connect Overview, Orders, Order Detail, Production, and Customers read APIs.

Exit: Admin reads real Staging data with role-aware responses.

## Phase 2 - Payment and generation

- Verify payment webhooks.
- Activate orders idempotently.
- Persist generation jobs and attempts.
- Process generation events/webhooks.
- Implement bounded retry and manual retry permission.
- Store book assets privately.

Exit: a paid test order appears and progresses to a ready Digital book.

## Phase 3 - Print workflow

- Generate and preflight Print Assets.
- Implement immutable approval.
- Create Print Jobs transactionally.
- Implement Partner Inbox and partner-scoped access.
- Implement sequential partner statuses.

Exit: an approved file reaches only the assigned partner with the same hash.

## Phase 4 - Courier

- Implement provider-neutral adapter.
- Create shipment only after `PACKED`.
- Add unique idempotency and reconciliation.
- Process courier webhooks.
- Show tracking and exceptions.

Exit: repeated clicks/retries never create a duplicate courier order.

## Phase 5 - Promotions, notifications, and audit

- Persist promotion rules and redemption.
- Integrate email/SMS notifications.
- Complete append-only Audit coverage.
- Add operational exports and reporting.

Exit: all privileged actions and provider changes are traceable.

## Phase 6 - Hardening and release

- Security review.
- Load and failure testing.
- Backup/restore test.
- Accessibility and responsive QA.
- Staging UAT with Admin and Print Partner.
- Production rollout and rollback drill.

## Migration principle

Replace one mock domain at a time. Do not rewrite the approved UI while backend
integration is in progress. Keep each phase releasable and testable.

