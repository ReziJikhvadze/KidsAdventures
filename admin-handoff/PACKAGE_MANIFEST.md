# Adventrya Admin Developer Handoff — Package Manifest

## Application source

- `app/` — approved Admin prototype routes, components, mock state, and styles.
- `public/` — locally bundled public assets.
- `integration/` — provider-agnostic TypeScript contracts and adapter boundaries.
- `db/` — current database scaffold. The production schema is specified in
  `docs/04_DATA_MODEL.md`.
- `worker/`, `build/`, `scripts/`, `tests/` — existing runtime, build, and
  verification tooling.

## Developer documentation

1. `README.md` — start here.
2. `HANDOFF_CHECKLIST.md` — implementation and release checklist.
3. `docs/01_CURRENT_STATE.md` — what is real and what is mocked.
4. `docs/02_ARCHITECTURE.md` — target system architecture.
5. `docs/03_ROUTE_AND_INTEGRATION_MAP.md` — UI-to-API mapping.
6. `docs/04_DATA_MODEL.md` — production data model and invariants.
7. `docs/05_STATE_MACHINES.md` — order, generation, print, and courier states.
8. `docs/06_API_CONTRACT.md` — API conventions and safety rules.
9. `docs/openapi.yaml` — OpenAPI 3.1 starter contract.
10. `docs/07_AUTH_RBAC.md` — roles, permissions, and partner isolation.
11. `docs/08_PRINT_AND_COURIER.md` — end-to-end production conveyor.
12. `docs/09_SECURITY_PRIVACY.md` — child-data and operational security.
13. `docs/10_IMPLEMENTATION_PLAN.md` — phased delivery plan.
14. `docs/11_QA_ACCEPTANCE.md` — acceptance scenarios.
15. `docs/12_OPEN_DECISIONS.md` — provider choices that remain open.
16. `docs/13_HANDOFF_VALIDATION.md` — packaging validation results.

## Product references

- `reference/Adventrya_Admin_Operator_Guide_v1.pdf`
- `reference/Adventrya_Admin_Prompt_1_UX_Architecture_Clickable_Prototype.md`
- `reference/Adventrya_Admin_Prompt_2_Final_UI_Polish.md`

## Intentionally excluded

- `node_modules/`
- build output (`dist/`, `.next/`, `.vinext/`, `.wrangler/`)
- runtime cache (`.sites-runtime/`)
- source-control history
- workspace-specific hosting identity
- credentials and production secrets

