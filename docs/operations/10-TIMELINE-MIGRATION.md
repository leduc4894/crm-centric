# Timeline storage migration

Using the migration role, apply `20261127040000_TimelineEntries` after `20261127036000_ExactNumberWhitespace`, followed by `20261127041000_AtomicContactNote`. Then apply the application-role grants and deploy the timeline endpoints. The timeline starts empty and existing contacts need no backfill.

The application role can read scoped entries and execute the note function. The function advances the contact version and records the timeline, audit and outbox entries in the same transaction. Rolling back the function disables new note writes and preserves existing entries.

Writers encode the summary as UTF-8 bytes after validating the contract length in Unicode scalar values. Readers decode those bytes as UTF-8. This preserves an embedded U+0000 and supplementary characters. External inbox references are scoped to the tenant, contact and entry kind.

A downgrade locks the table and checks every tenant. It drops the table only when empty. Once entries exist, preserve the data, take a backup and apply a forward fix. Follow the cursor signing key rollout procedure for timeline pagination. [Cursor key operations](08-CONTACT-CURSOR-KEY.md)
