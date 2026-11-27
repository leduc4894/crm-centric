# Timeline storage migration

Apply `20261127040000_TimelineEntries` after `20261127036000_ExactNumberWhitespace` using the migration role, then apply the application-role grants. The migration creates an empty tenant-scoped timeline. No existing contact rows require backfill. The application can read scoped entries. Note writes require an atomic writer that advances the contact version and records audit and outbox changes in the same transaction.

Writers encode the summary as UTF-8 bytes after validating the contract length in Unicode scalar values. Readers decode those bytes as UTF-8. This preserves an embedded U+0000 and supplementary characters. External inbox references are scoped to the tenant, contact and entry kind.

A downgrade locks the table and checks every tenant. It drops the table only when empty. Once entries exist, preserve the data, take a backup and apply a forward fix. Follow the cursor signing key rollout procedure for timeline pagination. [Cursor key operations](08-CONTACT-CURSOR-KEY.md)
