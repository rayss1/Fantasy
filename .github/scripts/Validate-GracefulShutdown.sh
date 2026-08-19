#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
host_output="$repository_root/artifacts/runtime-smoke/host"
stdout_log="$repository_root/artifacts/runtime-smoke/server-shutdown.stdout.log"
stderr_log="$repository_root/artifacts/runtime-smoke/server-shutdown.stderr.log"

if [[ ! -f "$host_output/Main.dll" ]]; then
  echo "Published Server Host was not found at $host_output/Main.dll." >&2
  exit 1
fi

host_pid=""
cleanup() {
  if [[ -n "$host_pid" ]] && kill -0 "$host_pid" 2>/dev/null; then
    kill -KILL "$host_pid" 2>/dev/null || true
    wait "$host_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT

(
  cd "$host_output"
  exec dotnet Main.dll -m Develop >"$stdout_log" 2>"$stderr_log"
) &
host_pid=$!

startup_deadline=$((SECONDS + 45))
while ! grep -q "Startup Complete" "$stdout_log" 2>/dev/null; do
  if ! kill -0 "$host_pid" 2>/dev/null; then
    wait "$host_pid" || true
    echo "Server Host exited before startup completed." >&2
    cat "$stdout_log" "$stderr_log" >&2
    exit 1
  fi
  if (( SECONDS >= startup_deadline )); then
    echo "Server Host did not start before the 45 second deadline." >&2
    cat "$stdout_log" "$stderr_log" >&2
    exit 1
  fi
  sleep 0.25
done

kill -TERM "$host_pid"
shutdown_deadline=$((SECONDS + 10))
while kill -0 "$host_pid" 2>/dev/null; do
  if (( SECONDS >= shutdown_deadline )); then
    echo "Server Host did not exit within 10 seconds of SIGTERM." >&2
    cat "$stdout_log" "$stderr_log" >&2
    exit 1
  fi
  sleep 0.1
done

set +e
wait "$host_pid"
exit_code=$?
set -e
host_pid=""

if [[ $exit_code -ne 0 ]]; then
  echo "Server Host exited with code $exit_code after SIGTERM." >&2
  cat "$stdout_log" "$stderr_log" >&2
  exit 1
fi

if ! grep -q "Shutdown Complete" "$stdout_log"; then
  echo "Server Host exited without confirming graceful shutdown." >&2
  cat "$stdout_log" "$stderr_log" >&2
  exit 1
fi

echo "Server Host completed graceful SIGTERM shutdown within 10 seconds."
