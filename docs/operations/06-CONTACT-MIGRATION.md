# Contact foundation migration

`20261127000000_ContactFoundation` adds tenant-scoped contacts, a durable event-intent table and an audit append function. Run the existing migrator with its separate migration and application roles; it checks role boundaries before and after migration. The CRM and outbox tables use raw SQL migrations rather than EF entities. Future schema changes must account for those objects explicitly.

The down migration is permitted only when there are no contacts, contact intents, or contact audit entries. It checks each tenant under forced RLS and aborts the transaction if it finds data. When data exists, take and verify a backup and create a reviewed forward migration; do not delete records merely to force a downgrade. A restore and recovery drill remains a separate acceptance requirement.

The outbox stores contact mutation intent in the same transaction as the business write and audit. It does not implement delivery or a consumer.
