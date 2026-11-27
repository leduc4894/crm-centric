#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "$0")/../../.." && pwd)
. "$root/scripts/lib/scratch-cleanup.sh"
tmp=$(mktemp -d)
trap 'scratch_remove "$tmp"' EXIT
mkdir -p "$tmp"/{old,new,feed,nuget}
# This immutable pre-upsert revision is a historical source fixture, not an
# advanced or trusted compatibility baseline. The regular gate still uses main.
git -C "$root" archive db04aeaecd7f4b6d083335a71418788d3c4561ca sdks/dotnet/Crm.Client | tar -x -C "$tmp/old" --strip-components=3
cp -R "$root/sdks/dotnet/Crm.Client/." "$tmp/new/"
for version in old new; do
  test -f "$root/tests/ClientConsumers/contact-upsert/$version/packages.lock.json"
done
cp -R "$root/tests/ClientConsumers/contact-upsert/old/." "$tmp/old-consumer"
cp -R "$root/tests/ClientConsumers/contact-upsert/new/." "$tmp/new-consumer"
image=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["dotnetImage"])' "$root/tools/contracts/toolchain.json")
docker run --rm -u "$(id -u):$(id -g)" -v "$tmp:/work" -w /work -e HOME=/work -e DOTNET_CLI_HOME=/work -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e NUGET_PACKAGES=/work/nuget "$image" sh -ec '
  for version in old new; do
    dotnet restore "/work/$version/Crm.Client.csproj" --locked-mode
    dotnet pack "/work/$version/Crm.Client.csproj" -c Release --no-restore -o /work/feed
  done
'
python3 "$root/tests/ClientConsumers/normalize_packages.py" "$tmp/feed"
docker run --rm -u "$(id -u):$(id -g)" -v "$tmp:/work" -w /work -e HOME=/work -e DOTNET_CLI_HOME=/work -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e NUGET_PACKAGES=/work/nuget "$image" sh -ec '
  for version in old new; do
    dotnet restore "/work/$version-consumer/Consumer.csproj" --locked-mode --source /work/feed --source https://api.nuget.org/v3/index.json
    dotnet run --project "/work/$version-consumer/Consumer.csproj" -c Release --no-restore
  done
'


# Old source against v2 must fail to compile: this is source break evidence,
# not a replacement for the historical consumer compatibility gate.
cp -R "$tmp/old-consumer" "$tmp/old-source-v2"
cp "$tmp/new-consumer/Consumer.csproj" "$tmp/old-source-v2/Consumer.csproj"
cp "$tmp/new-consumer/packages.lock.json" "$tmp/old-source-v2/packages.lock.json"
if docker run --rm -u "$(id -u):$(id -g)" -v "$tmp:/work" -w /work -e HOME=/work -e DOTNET_CLI_HOME=/work -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e NUGET_PACKAGES=/work/nuget "$image" sh -ec '
  dotnet restore /work/old-source-v2/Consumer.csproj --locked-mode --source /work/feed --source https://api.nuget.org/v3/index.json
  dotnet build /work/old-source-v2/Consumer.csproj -c Release --no-restore
' > "$tmp/old-source-v2.log" 2>&1; then
  echo "OLD_SOURCE_UNEXPECTEDLY_COMPILED" >&2
  exit 1
fi
if ! grep -Eq 'error CS(0029|7036)' "$tmp/old-source-v2.log"; then
  echo "OLD_SOURCE_BUILD_FAILED_FOR_UNEXPECTED_REASON" >&2
  exit 1
fi
echo OLD_SOURCE_V2_BREAK_CONFIRMED
