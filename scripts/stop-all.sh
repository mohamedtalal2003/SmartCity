#!/usr/bin/env bash
# Stops the services started by scripts/run-all.sh.
set -uo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
shopt -s nullglob

for pidfile in "$root"/.run/pids/*.pid; do
  name="$(basename "$pidfile" .pid)"
  pid="$(cat "$pidfile")"
  if kill -0 "$pid" 2>/dev/null; then
    kill "$pid" && echo "stopped $name (pid $pid)"
  else
    echo "$name was not running"
  fi
  rm -f "$pidfile"
done
