#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
# The scratch of this script is unlinked through an absolute rm binary, never
# through a PATH-resolved one: a recoverable-delete shim on the PATH would copy
# a multi-gigabyte scratch tree into the trash of the user instead of freeing
# it.
. "$root/scripts/lib/scratch-cleanup.sh"
tmp=$(mktemp -d)
trap 'scratch_remove "$tmp"' EXIT
mkdir -p "$tmp"/{typescript,packages,sdk}
cp -R "$root/sdks/typescript/." "$tmp/sdk/"
ln -s "$root/node_modules" "$tmp/sdk/node_modules"
for client in crm operations; do
  (cd "$tmp/sdk/$client" && "$root/node_modules/.bin/tsc" -p tsconfig.json && cp schema.d.ts dist/schema.d.ts && npm pack --ignore-scripts --pack-destination "$tmp/packages" --silent >/dev/null)
done
# npm ci guarantees locked installed tarballs, not cached registry packuments.
# Pack the exact complete runtime closure locally so this independent consumer
# needs no registry metadata or network, even with an entirely empty npm cache.
runtime_paths=$(python3 "$root/scripts/contracts/runtime_packages.py" "$root")
mapfile -t runtime_packages <<< "$runtime_paths"
npm pack --ignore-scripts --pack-destination "$tmp/packages" --silent "${runtime_packages[@]}" >/dev/null
cp "$root/tests/ClientConsumers/typescript/"* "$tmp/typescript/"
printf '{"name":"isolated-consumer","version":"1.0.0","private":true,"type":"module"}\n' > "$tmp/typescript/package.json"
(cd "$tmp/typescript" && npm install --offline --ignore-scripts --no-audit --no-fund "$tmp"/packages/*.tgz >/dev/null && "$root/node_modules/.bin/tsc" --noEmit --strict --module NodeNext --moduleResolution NodeNext --target ES2022 types.ts && node wire.mjs && {
  if SDK_TEST_WRONG_BEARER=1 node wire.mjs > "$tmp/negative.log" 2>&1; then exit 1; fi
  if grep -Fq 'synthetic-secret-sentinel-not-a-credential' "$tmp/negative.log"; then exit 1; fi
})
mkdir -p "$tmp/dotnet" "$tmp/dotnet-sdk" "$tmp/feed" "$tmp/nuget"
cp -R "$root/sdks/dotnet/." "$tmp/dotnet-sdk/"
mkdir -p "$tmp/new"
cp -R "$tmp/dotnet-sdk/Crm.Client/." "$tmp/new/"
scratch_remove "$tmp/dotnet-sdk/Crm.Client"
# Restore the exact pre-upsert SDK used by the historical consumer lock. This
# frozen input does not advance or replace the compatibility baseline.
git -C "$root" archive db04aeaecd7f4b6d083335a71418788d3c4561ca sdks/dotnet/Crm.Client | tar -x -C "$tmp/dotnet-sdk" --strip-components=2
cp "$root/tests/ClientConsumers/dotnet/"* "$tmp/dotnet/"
mkdir -p "$tmp/dotnet-v2" "$tmp/upsert" "$tmp/old-upsert"
cp "$root/tests/ClientConsumers/dotnet/Program.cs" "$tmp/dotnet-v2/Program.cs"
cp "$root/tests/ClientConsumers/dotnet-v2/"* "$tmp/dotnet-v2/"
cp "$root/tests/ClientConsumers/contact-upsert/new/"* "$tmp/upsert/"
cp "$root/tests/ClientConsumers/contact-upsert/old/Program.cs" "$tmp/old-upsert/Program.cs"
cp "$root/tests/ClientConsumers/dotnet/Consumer.csproj" "$root/tests/ClientConsumers/dotnet/packages.lock.json" "$tmp/old-upsert/"
image=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["dotnetImage"])' "$root/tools/contracts/toolchain.json")
docker run --rm -u "$(id -u):$(id -g)" -v "$tmp:/work" -w /work -e HOME=/work -e DOTNET_CLI_HOME=/work -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e NUGET_PACKAGES=/work/nuget "$image" sh -ec '
  for project in /work/dotnet-sdk/*/*.csproj /work/new/*.csproj; do
    dotnet restore "$project" --locked-mode
    dotnet pack "$project" -c Release --no-restore -o /work/feed
  done
'
python3 "$root/tests/ClientConsumers/normalize_packages.py" "$tmp/feed"
docker run --rm -u "$(id -u):$(id -g)" -v "$tmp:/work" -w /work -e HOME=/work -e DOTNET_CLI_HOME=/work -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e NUGET_PACKAGES=/work/nuget "$image" sh -ec '
  dotnet restore /work/dotnet/Consumer.csproj --locked-mode --source /work/feed --source https://api.nuget.org/v3/index.json
  dotnet run --project /work/dotnet/Consumer.csproj -c Release --no-restore
  if SDK_TEST_OMIT_EMAIL=1 dotnet run --project /work/dotnet/Consumer.csproj -c Release --no-build --no-restore >/work/negative-dotnet.log 2>&1; then exit 1; fi
  for consumer in dotnet-v2 old-upsert upsert; do
    dotnet restore "/work/$consumer/Consumer.csproj" --locked-mode --source /work/feed --source https://api.nuget.org/v3/index.json
    dotnet run --project "/work/$consumer/Consumer.csproj" -c Release --no-restore
    if [ "$consumer" = dotnet-v2 ] && SDK_TEST_OMIT_EMAIL=1 dotnet run --project "/work/$consumer/Consumer.csproj" -c Release --no-build --no-restore >/work/negative-dotnet-v2.log 2>&1; then exit 1; fi
  done
'
python3 "$root/tests/ClientConsumers/audit.py" "$tmp"
echo PACKAGED_CONSUMERS_OK
