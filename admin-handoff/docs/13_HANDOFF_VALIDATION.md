# Handoff Validation

Validation date: 2026-07-28

## Automated checks

| Check | Result |
|---|---|
| OpenAPI YAML parse | Passed |
| Required Admin/Partner API paths present | Passed |
| Dependency lockfile install | Passed |
| ESLint | Passed |
| Production build | Passed |
| Rendered HTML test | Passed |
| ZIP integrity test | Completed during packaging |
| Clean extraction test | Completed during packaging |

## Production build routes

- `/`
- `/audit`
- `/customers`
- `/fulfillment`
- `/login`
- `/orders`
- `/orders/:id`
- `/partner`
- `/production`
- `/promotions`
- `/settings`

## Handoff boundary

The included application is the approved frontend prototype and remains
mock-driven. Passing the build confirms that the prototype is technically
buildable; it does not mean that payment, generation, printing, courier,
authentication, notifications, storage, or audit persistence are connected.

The production integration requirements and safety constraints are defined in
the accompanying architecture, API, RBAC, data-model, and QA documents.

## Developer revalidation

After extraction:

```bash
npm run install:ci
npm run lint
npm run build
npm test
```

Before production release, the implementation must also pass all scenarios in
`docs/11_QA_ACCEPTANCE.md` against the real backend and sandbox provider
environments.

