# DemoModule — Cross-host E2E specification

## Independent service contract
- DemoModule process/container+DB riêng; CRM không reference source assembly.
- Exposes `/health`, manifest endpoint, contact panel endpoint (scoped), action endpoint (`demo.echo.v1`), event outbox.
- DemoModule has local object ID separate from CRM Contact ID; explicit link via integration UI/API.

## Test scenario
1. Boot CRM+IdP+DB, boot demo on another container network (simulate host separation).
2. CRM admin installs `com.example.demo` from manifest and approves scopes.
3. SSO opens demo external URL, verifies logged in user via OIDC.
4. Link external record `demo-member-001` to a CRM contact.
5. Demo emits valid signed CloudEvent `demo.activity.created.v1` with score 120.
6. CRM inbox persist+ack; project timeline; rule score>100 adds tag `engaged` and executes demo.echo remote action.
7. Resend same event 100 times with concurrency; one logical timeline item, tag and action effect.
8. Disable demo network → event pending, backoff and visible lag; reconnect → recovery.
9. Trigger schema mismatch and invalid signature → reject with reason token, auditable.
10. Disable installation → deny new actions, preserve history; uninstall → revoke credentials.

## Negative test matrix
- Wrong tenant / contact link; token wrong audience; tampered signature; expired timestamp; manifest SSRF; duplicate event id with different payload; two parallel workers; crashing after send before ack; modified manifest requiring new scope without approval.

## Proof artifacts
- CI logs and screenshots of installed module, contact panel and execution history.
- OpenTelemetry trace across inbound webhook→worker→automation.
- SQL assertions for inbox/outbox/action idempotency.
