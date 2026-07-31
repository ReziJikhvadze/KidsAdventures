# 03 - Route and Integration Map

| Route | Primary read API | Mutations |
| --- | --- | --- |
| `/` | `GET /admin/overview` | None |
| `/orders` | `GET /admin/orders` | Export request if server-generated |
| `/orders/:id` | `GET /admin/orders/:id` | Notes, refund/cancel request, PDF approval, courier creation |
| `/production` | `GET /admin/generation-jobs` | Retry eligible job |
| `/fulfillment` | `GET /admin/print-jobs` | Print status, courier creation |
| `/customers` | `GET /admin/customers`, `GET /admin/customers/:id` | Internal support note |
| `/promotions` | `GET /admin/promotions` | Create, pause, resume |
| `/audit` | `GET /admin/audit-events` | Export only |
| `/settings` | Roles, permissions, integrations, SLA endpoints | Role/SLA/config updates |
| `/partner` | `GET /partner/print-jobs` | Acknowledge and allowed status changes |
| `/login` | Identity provider | Session creation |

## UI-to-API replacement sequence

1. Introduce a typed API client and query cache.
2. Replace route-level reads one route at a time.
3. Keep current components and CSS.
4. Replace `AdminStateProvider` mutations with API mutations.
5. After all consumers are migrated, remove mock arrays and localStorage
   persistence.

## Recommended frontend integration layer

```text
app/
  api-client/
    http.ts
    orders.ts
    production.ts
    fulfillment.ts
    customers.ts
    promotions.ts
    audit.ts
  hooks/
    useOrders.ts
    useOrder.ts
    usePrintJobs.ts
  types/
    api.ts
```

Do not place provider-specific payment, generation, print, or courier code in
React components.

