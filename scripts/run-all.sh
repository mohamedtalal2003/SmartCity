#!/usr/bin/env bash
# Builds the solution and starts every service + the Gateway in the background (Development).
# Logs: .run/logs/<project>.log   PIDs: .run/pids/<project>.pid   Stop: scripts/stop-all.sh
# Infrastructure must already be up: docker compose up -d
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
mkdir -p .run/logs .run/pids

dotnet build SmartCity.sln --nologo -v quiet

export ASPNETCORE_ENVIRONMENT=Development

# Every web project under src/Gateway and src/Services (class libraries are skipped).
for csproj in src/Gateway/*/*.csproj src/Services/*/*/*.csproj; do
  grep -q 'Sdk="Microsoft.NET.Sdk.Web"' "$csproj" || continue
  dir="$(dirname "$csproj")"
  name="$(basename "$csproj" .csproj)"
  pidfile=".run/pids/$name.pid"

  if [ -f "$pidfile" ] && kill -0 "$(cat "$pidfile")" 2>/dev/null; then
    echo "$name already running (pid $(cat "$pidfile"))"
    continue
  fi

  # Run the built dll from the project folder (content root = appsettings location).
  (cd "$dir" && nohup dotnet "bin/Debug/net8.0/$name.dll" > "$root/.run/logs/$name.log" 2>&1 & echo $! > "$root/$pidfile")
  echo "started $name (pid $(cat "$pidfile"))"
done
