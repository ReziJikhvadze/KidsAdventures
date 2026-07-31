# Developer Handoff Checklist

## Package integrity

- [ ] `npm ci` completes successfully.
- [ ] `npm run lint` passes.
- [ ] `npm run build` passes.
- [ ] All routes listed in `README.md` open.
- [ ] No secrets are committed.
- [ ] `.env.example` contains names only, never real values.

## Before backend implementation

- [ ] Confirm production hosting and backend stack.
- [ ] Confirm authentication provider.
- [ ] Confirm payment provider and webhook format.
- [ ] Confirm generation service event format.
- [ ] Confirm Print Partner delivery method.
- [ ] Confirm courier provider and webhook format.
- [ ] Confirm object storage provider and retention.
- [ ] Confirm notification providers.

## Backend integration

- [ ] Replace `app/data.ts` mock reads with API queries.
- [ ] Replace `AdminStateProvider` mutations with server actions/API mutations.
- [ ] Move all authorization checks to the backend.
- [ ] Implement database constraints from `docs/04_DATA_MODEL.md`.
- [ ] Implement state transition guards from `docs/05_STATE_MACHINES.md`.
- [ ] Implement idempotency for payments, print approval, and courier creation.
- [ ] Implement append-only Audit Events.
- [ ] Implement signed, expiring file access.

## Security

- [ ] Print Partner cannot query unassigned Print Jobs.
- [ ] Print Partner responses contain no prohibited PII.
- [ ] Courier payload contains only delivery-required PII.
- [ ] Approved print assets cannot be overwritten.
- [ ] Audit Events cannot be edited or deleted through the application.
- [ ] Refund, cancellation, role, and approval actions require permission checks.
- [ ] All privileged mutations are protected against CSRF/replay as applicable.

## Release

- [ ] Staging environment uses test providers.
- [ ] Required QA scenarios pass.
- [ ] Logs and error monitoring are configured.
- [ ] Backup and restore are tested.
- [ ] Production secrets are set outside source control.
- [ ] Rollback procedure is documented and tested.

