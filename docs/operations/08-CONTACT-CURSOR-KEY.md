# Contact list cursor key operations

Set `ContactCursor:SigningKey` from the deployment secret store (environment variable `ContactCursor__SigningKey`), to a base64 encoding of at least 32 cryptographically random bytes. Provision it securely before serving contact lists; never commit, print or log its value. The list endpoint must fail closed with a correlated configuration error when the key is missing or invalid. Other endpoints need not depend on this configuration.

Every API instance serving the same deployment must receive the same key. An instance with a different key rejects cursors issued by its peers. When rotating the key, update all instances together according to the deployment's secret rollout procedure. Rotation invalidates existing cursors: clients must restart pagination at the first page. Cursor tokens are not authentication credentials; every request still requires tenant authorization. Tokens contain no plaintext filter text, but should not be logged.

For the local Compose stack, securely set `CONTACT_CURSOR_SIGNING_KEY` in the invoking environment or local secret management before starting the stack. Compose maps it to the API configuration; no default key is supplied. Integration test factories generate an ephemeral random test key at runtime and permit explicit overrides for key mismatch cases. Do not reuse a test key in a deployment.
