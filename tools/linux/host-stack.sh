#!/usr/bin/env bash
# Starts the whole Phase 5 stack for local testing on Linux:
#   gateway (YARP)  http://localhost:5000   <- talk to this one
#   Legacy.Web      http://localhost:5080   (Mono + nginx; stands in for IIS)
#   Modern.Web      http://localhost:5100
#
# Usage: tools/linux/host-stack.sh [stop]
#   MIGRATION_PAGES='Dashboard=Legacy;Talks=Modern'   override where individual pages live
#   AUTH_ACCEPT_LEGACY_SESSION=true                   let Modern.Web accept a login made on Legacy.Web
# Requires SQL Server on :1433 (docker compose up -d) and the Mono prerequisites of host-legacy.sh.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
LOGS=/tmp/confreg; mkdir -p "$LOGS"

stop() {
  pkill -f ConfReg.Modern.Web.dll 2>/dev/null || true
  pkill -f ConfReg.Gateway.dll 2>/dev/null || true
  "$ROOT/tools/linux/host-legacy.sh" stop >/dev/null 2>&1 || true
}
[ "${1:-}" = "stop" ] && { stop; echo stopped; exit 0; }
stop

: "${ConfReg__ConnectionString:=Server=localhost,1433;Database=ConfReg;User Id=sa;Password=ConfReg_Dev_Passw0rd!;TrustServerCertificate=True;}"
export ConfReg__ConnectionString
# Must match the legacy app's ConfReg:RemoteAppApiKey (32 hex chars) or the shared session cannot be fetched.
: "${ConfReg__RemoteAppApiKey:=deadbeefdeadbeefdeadbeefdeadbeef}"
export ConfReg__RemoteAppApiKey
export Auth__AcceptLegacySession="${AUTH_ACCEPT_LEGACY_SESSION:-false}"
# Modern.Web needs the LEGACY crypto key too: admin rows written before Phase 3 are AES ciphertext, and whichever app
# serves the login has to verify one of those once before it can re-hash it with BCrypt. Demo values; see config/secrets.config.example.
: "${ConfReg__LegacyCryptoKey:=Y29uZnJlZy1kZW1vLWtleS1OT1QtQS1TRUNSRVQtMDA=}"
: "${ConfReg__LegacyCryptoIv:=Y29uZnJlZy1kZW1vLWl2IQ==}"
: "${ConfReg__PaymentApiKey:=sk_test_DEMO_not_a_real_key}"
export ConfReg__LegacyCryptoKey ConfReg__LegacyCryptoIv ConfReg__PaymentApiKey

# Legacy first: Modern.Web fetches session from it, and it owns creating/seeding the database.
"$ROOT/tools/linux/host-legacy.sh" >/dev/null
echo "Legacy.Web  http://localhost:5080"

dotnet build "$ROOT/src/Modern.Web" -v q
dotnet build "$ROOT/src/Gateway" -v q

nohup dotnet "$ROOT/src/Modern.Web/bin/Debug/net10.0/ConfReg.Modern.Web.dll" > "$LOGS/modern.log" 2>&1 &
# Per-page overrides: Migration__Pages__<Page>=Legacy|Modern
if [ -n "${MIGRATION_PAGES:-}" ]; then
  IFS=';' read -ra pairs <<< "$MIGRATION_PAGES"
  for pair in "${pairs[@]}"; do export "Migration__Pages__${pair%%=*}=${pair##*=}"; done
fi
nohup dotnet "$ROOT/src/Gateway/bin/Debug/net10.0/ConfReg.Gateway.dll" > "$LOGS/gateway.log" 2>&1 &

for i in $(seq 1 40); do
  if curl -fs http://localhost:5100/health/live >/dev/null && curl -fs http://localhost:5000/gateway/health >/dev/null; then
    echo "Modern.Web  http://localhost:5100"
    echo "Gateway     http://localhost:5000   (routes: /gateway/routes)"
    curl -s http://localhost:5000/gateway/routes; echo
    exit 0
  fi
  sleep 1
done
echo "stack did not start; see $LOGS/modern.log and $LOGS/gateway.log" >&2
tail -5 "$LOGS/modern.log" "$LOGS/gateway.log" >&2 || true
exit 1
