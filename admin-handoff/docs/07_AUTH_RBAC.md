# 07 - Authentication and RBAC

## Roles

### Super Admin

- full operational visibility;
- approve print assets;
- manage roles and integration settings;
- request refunds/cancellations;
- export sensitive reports.

### Operations

- manage orders, generation, print, and delivery;
- approve print if explicitly granted;
- create courier shipments;
- view necessary parent contacts;
- cannot change role definitions by default.

### Customer Support

- search customers and orders;
- view contact details;
- add internal notes;
- request escalation;
- no print approval, courier creation, or role management.

### Finance

- payment and promotion reporting;
- refund workflow according to permission;
- no child photo/generation prompt access unless required.

### Print Partner

- view only assigned Print Jobs;
- access only the approved print asset;
- acknowledge and update allowed production statuses;
- no parent email/phone/address;
- no child photo, interests, prompt, payment, promotion, or margin data;
- no other partner's jobs.

### Read-only Auditor

- read operational and Audit records;
- no mutations;
- sensitive fields may be masked according to policy.

## Permission examples

- `orders.read`
- `orders.export`
- `customers.read_pii`
- `generation.retry`
- `print_assets.read`
- `print_assets.approve`
- `print_jobs.manage`
- `courier_shipments.create`
- `payments.read`
- `refunds.request`
- `promotions.manage`
- `audit.read`
- `roles.manage`
- `settings.manage`

## Enforcement

- Protect routes for usability.
- Protect every API endpoint for security.
- Filter records by tenant/partner assignment on the backend.
- Never depend on hidden buttons or client-side route guards.
- Include role snapshot in Audit Events.
- Revoke sessions promptly when access is removed.

## Session requirements

- Secure, HTTP-only, same-site cookies where applicable.
- Short-lived access session with controlled refresh.
- MFA strongly recommended for Super Admin and financial/approval roles.
- Reauthentication for high-risk actions can be added after MVP.

