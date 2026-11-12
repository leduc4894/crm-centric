# Keycloak 26.8.2 remediation investigation (not adopted)

This is a local diagnostic record from 2026-11-12, not a replacement runtime lock,
verified upstream build, license clearance or accepted release. Defaults and CI
identity/freshness/finding policies remain unchanged. Read the same-day
[handoff](../../../handoff/2026-11-12-wave-0-plan-4-remediation-handoff.md).

The exact configured upstream digest was pulled and scanned by pinned Trivy
0.76.2: 559 packages and six HIGH findings (one OpenJDK, four SQLite, one embedded
Guava). The normalized observations are in `diagnostic-results.json`; raw reports
were captured privately and deleted. Scans used existing metadata with
`--skip-db-update --skip-java-db-update`; they are diagnostic, without a freshness
or mandatory gate clearance claim. A separate full-license scan found 52 rows and
20 identifiers outside the existing allowlist. No identifier was approved.

[Official downloads](https://www.keycloak.org/downloads) still names 26.8.2;
an actual registry lookup for 26.8.3 failed. This does not establish a release
schedule. The mutable 26.8.2 tag was already rescanned in the previous continuation
and retained the six findings; blindly refreshing that tag is insufficient.

[Upstream's versioned Dockerfile](https://raw.githubusercontent.com/keycloak/keycloak/26.8.2/quarkus/container/Dockerfile)
builds an OpenJDK21 UBI9 root filesystem. Its documented
[container packaging pattern](https://www.keycloak.org/server/containers) permits
an installroot build using signed distro packages. Avoid copying arbitrary shared
libraries between distros or removing SQLite merely to hide its inventory.
[Supported configurations](https://www.keycloak.org/server/supported-configurations)
list OpenJDK17/21/25 and PostgreSQL18. This supports investigating a maintained
JDK distribution; it does not endorse a particular downstream image or establish
this project's auth compatibility.

The Guava finding is independent of the base/JDK. The
[official Guava33.7.2 release](https://github.com/google/guava/releases/tag/v33.7.2)
explicitly fixes CVE-2026-102554; the actual distribution scan identifies33.6.0-jre.
Moving the same distribution to a supported newer JDK leaves that vulnerable JAR.
Replacing a single JAR without rebuilding Quarkus dependency/index metadata and
testing the dependency closure is not an established supported replacement.
Do not rename/delete JAR metadata or claim absence of vulnerable functionality
from PostgreSQL selection alone.

[Red Hat's Java advisory](https://access.redhat.com/security/cve/CVE-2026-91403)
explains that an initial intermediate-certificate block was insufficient; the
related follow-up is CVE-2026-114820. The
[SQLite advisory](https://access.redhat.com/security/cve/CVE-2026-119373)
describes its FTS5 preconditions. Those descriptions are not waivers. Local
scanner findings remain blocking even when applicability or vendor status needs
independent review.

Actual signed-repository build probe: copied the exact pinned distribution into
UBI9.8 at immutable digest recorded in JSON; ran normal `dnf upgrade --refresh`
and installed OpenJDK21, English locale and findutils with `gpgcheck=1`, retaining
normal vendor repository keys. Build PASS. Scan of its actual config ID found
722 packages and 36 HIGH findings, including the same Java/SQLite/Guava six.
The full UBI diagnostic also adds tools/libraries and their findings; it is not
an optimized micro-rootfs candidate and does not prove every minimal UBI build
has those additional findings. It does demonstrate that this normal signed
repository refresh did not provide fixed Java/SQLite packages. Repository state
was not snapshotted; no reproducible-build or production provenance claim. Its
own temporary Dockerfile and image tag were removed. No foreign resources were
changed. No compatibility tests were run on this already-blocked probe.

The exact upstream `ubi-null.sh` from the resolved26.8.2 source tag was also
executed with its unchanged package-pruning algorithm and standard vendor GPG
verification. Its current JDK21 rebuild produced559 packages and the original
six HIGH findings. Substituting the supported official
`java-25-openjdk-headless` package produced560 packages/seven HIGH: the same
SQLite4/Guava1 and the Java CA finding in both headless and crypto-adapter RPMs.
No OS reduction was demonstrated. A simpler installroot-only experiment produced
602 packages/24 HIGH; the upstream pruning pattern avoids those extra packages.
These are online diagnostic builds, not snapshotted reproducible releases.
The minimal probes used the versioned micro9.8 final-base tag; its contemporaneous
lookup digest is recorded, but their Dockerfiles did not digest-pin that final
base. Their output config IDs were actually scanned; do not infer immutable
input closure from those diagnostic output identities.

A supported Temurin25.0.5+8 Noble JRE image was resolved and pulled by the immutable
digest in JSON. Its scan reports108 packages/zero HIGH, but an actual
`java -version` confirms25.0.5 while Trivy inventories only Ubuntu packages and
**no Java/JDK/JRE/Temurin package**. This is a demonstrated runtime coverage gap,
not a security fix. Replacing RPM-visible Java with this tar-installed JRE cannot
clear the existing requirement without independently supported runtime closure
and vulnerability evidence. [Temurin's support policy](https://adoptium.net/support)
provides a supported Java25 LTS stream, not scanner coverage or full license approval.
An initial mutable-tag probe failed; the explicit25.0.5_8 image probe succeeded.

Source-build feasibility uses the official26.8.2 tag resolved through GitHub's
API to commit `e514445f9dcdabc5bdcf67cab56c1e49655de067`; archive SHA256 and the
verified Maven3.9.16 archive SHA512 are recorded. No source-tag signature
verification is claimed. The project's versioned
[building instructions](https://github.com/keycloak/keycloak/blob/26.8.2/docs/building.md)
support JDK25 and its server-only reactor. The reviewable
[patch](guava-source.patch) adds one root dependencyManagement override for
Guava33.7.2-jre before rebuilding source; it does not swap a JAR in a vendor binary.
Patch application against the unmodified root POM was actually checked.
The pinned Maven/JDK25 container initially failed the pnpm/Kiota code-generation
stage; actual inspection found no ICU library and the failure stack enters
GlobalizationMode. A normal signed Ubuntu build-only install of libicu74
74.2-1ubuntu3.1 supplies that prerequisite. No invariant-globalization toggle,
compiler bypass, license-plugin bypass or dependency-rule bypass was added.
Feasibility builds use the documented `-DskipTests` option, which does not establish
upstream test success or product compatibility. Maven's downloaded cache is not
a complete independently verified dependency lock; snapshot/reproducibility and
complete build-tool closure remain requirements before an adopted downstream release.

Actual source result: after adding ICU, the server-only reactor completed in
224.962s with BUILD SUCCESS and produced both distribution archives. The three
initial attempts took177.007/54.551/68.281s and produced no accepted distribution. The final diagnostic attempt localized
the failure to the ICU/Kiota prerequisite; the first two did not retain sufficient
output to independently attribute their cause. Maven cache grew to811MiB.
The resulting tar.gz SHA256 is in JSON. A fresh UBI21/micro image, digest-pinned
at both base stages, was built from the complete new distribution with the
unchanged upstream rootfs pattern; `kc.sh build --db=postgres --health-enabled=true`
completed. Its actual config scan reports559 packages and **five HIGH**; Guava is
honestly inventoried as33.7.2-jre and its previous finding is absent. Remaining
Java1/SQLite4 still block adoption. [All483 observed JAR hashes](diagnostic-runtime-jars.json)
are a runtime observation, not a complete Maven build lock or reproducibility proof.
The source patch is therefore a demonstrated component remediation, not a passing
image or product release. No fresh mandatory `scan_image` invocation, source
runtime full-license review, repeated identical-output build, service auth/DB suite
or acceptance drill was run. The blocked image's own tag was removed after scan;
no wrapper/service was launched. The existing full upstream test instructions
warn that the full suite may take several hours; no cost-free support claim is
inferred from this approximately four-minute successful feasibility build.

Options requiring concrete subsequent work:

1. Wait for a supported upstream distribution containing patched Guava and a
   clean supported base/JDK; pull by immutable identity and run fresh exact-image
   scans. Keep the current default blocked until then.
2. Maintain a downstream Keycloak source build with a reviewed Guava33.7.2-jre
   dependency update and complete transitive closure, plus signed fixed UBI RPMs
   or a verified supported JDK25 distribution. Pin source/compiler/base/package
   checksums, verify upstream signatures where available, preserve dependency
   notices, prove repeated builds and test the resulting exact image. This adds
   ongoing ownership; it is not an upstream-supported Keycloak release.
3. Investigate a separately supported vendor image with documented patch
   provenance, distribution/license terms and publicly obtainable immutable
   inputs. No vendor candidate was verified here; do not assert support from
   a scanner result or choose a paid/restricted artifact implicitly.

Before adoption, run the existing mandatory image gate and complete full license
review, then real PostgreSQL identity tests on the exact final wrapper image:
nonroot UID and secret permissions, private management health, initial realm
import, two public PKCE-S256 clients with direct/implicit/service grants disabled,
canonical issuer, restricted separate IDP DB role, restart persistence and DB
outage/recovery. Run real browser authorization-code/PKCE against the backend,
issuer/sub linkage and tenant isolation checks, concurrent duplicate, backup
restore and remote DemoModule acceptance. Keep the issuer/realm/client settings
and schema stable; preserve the original DB backup and image for rollback, freeze
writes or establish reconciliation before testing any DB migration. No such
compatibility, migration or acceptance suite ran in this investigation because
no policy-clean supported candidate was established.
