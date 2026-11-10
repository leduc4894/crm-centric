#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
mkdir -p "$tmp"/{typescript,packages,sdk}
cp -R "$root/sdks/typescript/." "$tmp/sdk/"
ln -s "$root/node_modules" "$tmp/sdk/node_modules"
for client in crm operations; do
  (cd "$tmp/sdk/$client" && "$root/node_modules/.bin/tsc" -p tsconfig.json && cp schema.d.ts dist/schema.d.ts && npm pack --ignore-scripts --pack-destination "$tmp/packages" --silent >/dev/null)
done
cp "$root/tests/ClientConsumers/typescript/"* "$tmp/typescript/"
printf '{"name":"isolated-consumer","version":"1.0.0","private":true,"type":"module"}\n' > "$tmp/typescript/package.json"
(cd "$tmp/typescript" && npm install --offline --ignore-scripts --no-audit --no-fund "$tmp"/packages/*.tgz >/dev/null && "$root/node_modules/.bin/tsc" --noEmit --strict --module NodeNext --moduleResolution NodeNext --target ES2022 types.ts && node wire.mjs && {
  if SDK_TEST_WRONG_BEARER=1 node wire.mjs > "$tmp/negative.log" 2>&1; then exit 1; fi
  if grep -Fq 'synthetic-secret-sentinel-not-a-credential' "$tmp/negative.log"; then exit 1; fi
})
mkdir -p "$tmp/dotnet" "$tmp/dotnet-sdk" "$tmp/feed" "$tmp/nuget"
cp -R "$root/sdks/dotnet/." "$tmp/dotnet-sdk/"
cp "$root/tests/ClientConsumers/dotnet/"* "$tmp/dotnet/"
image=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["dotnetImage"])' "$root/tools/contracts/toolchain.json")
docker run --rm -u "$(id -u):$(id -g)" -v "$tmp:/work" -w /work -e HOME=/work -e DOTNET_CLI_HOME=/work -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e NUGET_PACKAGES=/work/nuget "$image" sh -ec '
  for project in /work/dotnet-sdk/*/*.csproj; do
    dotnet restore "$project" --locked-mode
    dotnet pack "$project" -c Release --no-restore -o /work/feed
  done
'
python3 "$root/tests/ClientConsumers/normalize_packages.py" "$tmp/feed"
docker run --rm -u "$(id -u):$(id -g)" -v "$tmp:/work" -w /work -e HOME=/work -e DOTNET_CLI_HOME=/work -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e NUGET_PACKAGES=/work/nuget "$image" sh -ec '
  dotnet restore /work/dotnet/Consumer.csproj --locked-mode --source /work/feed --source https://api.nuget.org/v3/index.json
  dotnet run --project /work/dotnet/Consumer.csproj -c Release --no-restore
  if SDK_TEST_OMIT_EMAIL=1 dotnet run --project /work/dotnet/Consumer.csproj -c Release --no-build --no-restore >/work/negative-dotnet.log 2>&1; then exit 1; fi
'
python3 "$root/tests/ClientConsumers/audit.py" "$tmp"
echo PACKAGED_CONSUMERS_OK
