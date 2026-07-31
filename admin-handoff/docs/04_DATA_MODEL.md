# 04 - Production Data Model

The final schema can use PostgreSQL, another relational database, or a managed
equivalent. The constraints below are more important than the vendor.

## Core entities

### Identity and customer

- `admin_users`
- `roles`
- `permissions`
- `role_permissions`
- `admin_user_roles`
- `parent_users`
- `child_profiles`
- `characters`
- `addresses`

### Book and order

- `books`
- `book_characters`
- `book_pages`
- `orders`
- `order_items`
- `payments`
- `promotions`
- `promotion_redemptions`

### Generation

- `generation_jobs`
- `generation_attempts`
- `generation_events`

### Print and delivery

- `print_assets`
- `print_approvals`
- `print_partners`
- `print_jobs`
- `print_job_events`
- `courier_shipments`
- `courier_events`

### Platform operations

- `audit_events`
- `notifications`
- `idempotency_keys`
- `outbox_events`

## Critical fields

### `orders`

- `id`
- `public_order_number` - unique human-readable ID such as `ADV-1046`
- `parent_user_id`
- `status`
- `purchase_type`: `DIGITAL`, `PRINTED_DIGITAL`, `PRINT_UPGRADE`
- `currency`: `GEL`
- `subtotal_amount`
- `discount_amount`
- `total_amount`
- `promotion_id` nullable
- `delivery_address_snapshot` JSON, immutable after fulfillment starts
- `created_at`, `updated_at`

### `books`

- `id`
- `parent_user_id`
- `primary_child_profile_id`
- `title`
- `language`: `KA`, `EN`
- `theme`
- `status`
- `digital_owned`
- `print_owned`
- `source_book_id` nullable for continuation
- `generation_fingerprint`
- `created_at`

Purchased books must not be rewritten when a child profile changes.

### `print_assets`

- `id`
- `book_id`
- `version`
- `object_key`
- `mime_type`
- `size_bytes`
- `sha256`
- `page_count`
- `preflight_status`
- `qr_verified`
- `created_at`

Unique: `(book_id, version)`.

### `print_approvals`

- `id`
- `order_id`
- `print_asset_id`
- `approved_by_admin_user_id`
- `approved_at`
- `asset_sha256_snapshot`
- `asset_version_snapshot`

Only one active approval per Print Job. An approval cannot be updated; a new
version requires an explicit superseding workflow and Audit Event.

### `print_jobs`

- `id`
- `order_id`
- `print_partner_id`
- `approved_print_asset_id`
- `status`
- `due_at`
- `accepted_at`
- `packed_at`
- `current_owner`
- `created_at`, `updated_at`

Unique: one active Print Job per printed order/order item.

### `courier_shipments`

- `id`
- `order_id`
- `print_job_id`
- `provider`
- `idempotency_key`
- `external_order_id`
- `tracking_id`
- `status`
- `request_snapshot` JSON
- `response_snapshot` JSON
- `created_at`, `updated_at`

Unique:

- `idempotency_key`
- `external_order_id` when not null
- one non-cancelled shipment per `print_job_id`

### `audit_events`

- `id`
- `occurred_at`
- `actor_type`
- `actor_id`
- `actor_role_snapshot`
- `category`
- `action`
- `target_type`
- `target_id`
- `order_id` nullable
- `before_snapshot` JSON nullable
- `after_snapshot` JSON nullable
- `request_id`
- `ip_address` nullable
- `metadata` JSON

Application roles may insert and read according to permission, but must not
update or delete Audit Events.

## Required database constraints

1. A Print Job references an approved Print Asset.
2. A courier shipment references a `PACKED` Print Job at creation time,
   enforced in the service transaction.
3. Idempotency keys are unique.
4. Monetary amounts are integers in minor units, never floating point.
5. Promotion redemption is unique per configured rule.
6. Status changes use optimistic locking or row locks.
7. Child photos and print files are stored in private object storage, not as
   public URLs.

