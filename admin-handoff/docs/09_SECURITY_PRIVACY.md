# 09 - Security and Privacy

Adventrya handles child data and must use a higher privacy standard than a
generic commerce dashboard.

## Data classification

### Highly sensitive

- child portrait photo;
- generated likeness/illustrations;
- birth date;
- interests and story prompt;
- book content;
- parent-child relationship data.

### Personal

- parent name;
- email;
- phone;
- delivery address;
- payment references.

### Operational

- status;
- Print Job identifiers;
- technical print specifications;
- courier tracking.

## Required controls

- encryption in transit and at rest;
- private object storage;
- short-lived signed file URLs;
- least-privilege RBAC;
- partner-scoped queries;
- field-level response DTOs;
- secret management outside source control;
- verified webhooks;
- rate limiting;
- input validation;
- structured error monitoring;
- append-only Audit Events;
- backup and tested restore;
- explicit retention and deletion policy.

## Logging rules

Never write to logs:

- raw child photos/files;
- full generation prompts;
- full payment payloads;
- authentication tokens;
- provider secrets;
- signed file URLs;
- full phone/email/address unless an approved secure support log requires it.

## File rules

- Use random object keys, not customer names.
- Store checksum and content type.
- Validate uploads and generated PDFs.
- Malware-scan externally supplied files when applicable.
- Expire signed URLs.
- Do not let a partner enumerate object-storage paths.

## Audit rules

Audit:

- login/access changes;
- PII views/exports where required;
- print approvals;
- partner assignments;
- production status changes;
- courier creation/cancellation;
- promotion changes;
- refunds/cancellations;
- role and SLA configuration.

## Retention

The final retention schedule must be approved with legal/accounting input. The
implementation must support:

- guest data deletion after the configured period;
- account deletion workflow;
- legal/accounting retention exceptions;
- removal of expired signed access;
- deletion/anonymization without corrupting financial Audit requirements.

