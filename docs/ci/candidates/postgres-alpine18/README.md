# PostgreSQL18.6 Alpine downstream candidate (not adopted)

This review artifact records real local inputs from 2026-11-12, not a production
image lock, approved architecture, independent attestation or accepted release.
The current Bookworm default remains unchanged. See the same-day remediation
[handoff](../../../handoff/2026-11-12-wave-0-plan-4-remediation-handoff.md).

`diagnostic-inputs.json` records immutable upstream image/source identity, actual
Go archive checksum verified against https://go.dev/dl/?mode=json, exact downstream
module contents and signed Alpine package SHA256 closure. APK bytes are not
committed. Versioned mirror URLs may disappear; long-term content-addressed storage
and independent provenance remain to be designed before production adoption.
The upstream gosu tag was resolved through GitHub's tag-ref API to the recorded
commit; no claim of verified maintainer signature on that source commit is made.

Actual build sequence was source checkout at that commit; change only x/sys to
v0.44.0 with Go's module tooling (raising minimum Go to1.25.0); `go mod verify`;
then static `go build -trimpath -buildvcs=true`. Retain honest dirty VCS metadata
for the downstream change. Building with `-buildvcs=false` omitted the main-module
version in Trivy and correctly failed the mandatory image inventory check.
The compiler archive and module sum verification passed. Two initial builds with
`-buildvcs=false` matched byte-for-byte; repeat builds of the final VCS-aware binary
and complete image have NOT been demonstrated.

For the offline image probe, verified all12 APK file checksums, copied those files
into a derivative of the recorded base, ran `apk add --no-network /tmp/pinned-apks/*.apk`
with the base's signature keys and no signature bypass, deleted that directory,
and replaced `/usr/local/bin/gosu` with the final static binary. Actual Docker
build used `--network=none` and passed. The copied files in an earlier image layer
remain image contents; production packaging should avoid retaining build inputs
and establish complete image reproducibility. This is a proposed packaging
improvement, not implemented here.

The actual repository `scan_image` passed on the recorded offline config:57
packages, zero vulnerability findings, fresh vulnerability/Java metadata and
normalized SBOM. This image did not include the repository wrapper; wrapper first
init/restart/idp-role tests used the earlier online-upgraded derivative containing
the same gosu binary. Do not claim the offline candidate passed those tests.

The separate full license scan returned71 rows and15 identifiers outside the
existing license allowlist, including GPL/LGPL, MPL, ICU, OLDAP, X-11, curl and
Zlib. No license was approved and no rule was relaxed. Existing image scanning
checks vulnerabilities and SBOM; it does not adjudicate those license rows.

The strongest demonstrated route is a maintained downstream image from signed
Alpine packages and patched gosu. Costs include ownership of source/module/build
provenance, updates, licensing and compatibility, rather than a new image pin.
Alpine changes glibc to musl, UID999 to70 and locale/collation implementation.
Never mount the only Debian data volume directly into this candidate. Before
adoption, use a copied backup and isolated new storage; prove ownership/permissions,
logical dump/restore, collation/index ordering and constraints, tenant/concurrency,
API/Keycloak operation and outage/recovery. Keep the original Debian backup/image
available and prove rollback with writes frozen or an explicit data-reconciliation
plan. No such migration, full integration, restore or rollback drill ran here.
