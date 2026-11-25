#!/usr/bin/env bash
# Cold start: how long from "process launched" to "first useful admin page served".
#
# Measured as process start -> login -> dashboard returns 200, because that is the whole cost a first visitor pays:
# runtime start, JIT, DI/route building, EF6 model build, connection pool, and the first query. Measuring a trivial
# endpoint instead would flatter both apps and hide the EF model build, which dominates.
#
# The database is NOT reset between repeats; the admin password upgrade to BCrypt happens on the first login of the run
# and would otherwise be inside the first measurement only.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$ROOT/tools/bench/results"; mkdir -p "$OUT"
REPEATS="${REPEATS:-3}"
LOGS=/tmp/confreg; mkdir -p "$LOGS"

: "${ConfReg__ConnectionString:=Server=localhost,1433;Database=ConfReg;User Id=sa;Password=ConfReg_Dev_Passw0rd!;TrustServerCertificate=True;}"
: "${ConfReg__RemoteAppApiKey:=deadbeefdeadbeefdeadbeefdeadbeef}"
: "${ConfReg__LegacyCryptoKey:=Y29uZnJlZy1kZW1vLWtleS1OT1QtQS1TRUNSRVQtMDA=}"
: "${ConfReg__LegacyCryptoIv:=Y29uZnJlZy1kZW1vLWl2IQ==}"
: "${ConfReg__PaymentApiKey:=sk_test_DEMO_not_a_real_key}"
export ConfReg__ConnectionString ConfReg__RemoteAppApiKey ConfReg__LegacyCryptoKey ConfReg__LegacyCryptoIv ConfReg__PaymentApiKey
export Auth__AcceptLegacySession=false

# time_to_ready <base-url> : milliseconds until login+dashboard both succeed
time_to_ready() {
  local base="$1" start now jar
  start=$(date +%s%N)
  for _ in $(seq 1 2000); do
    jar=$(mktemp)
    if curl -fsS -c "$jar" -b "$jar" -o /dev/null --max-time 20 \
         --data "Email=admin@confreg.example&Password=ConfReg-Demo-1!" "$base/Account/Login" 2>/dev/null \
       && [ "$(curl -s -o /dev/null -w '%{http_code}' -b "$jar" --max-time 20 "$base/Admin?format=json")" = "200" ]; then
      now=$(date +%s%N); rm -f "$jar"; echo $(( (now - start) / 1000000 )); return 0
    fi
    rm -f "$jar"; sleep 0.05
  done
  echo "TIMEOUT"; return 1
}

say() { printf '\n== %s\n' "$*"; }
: > "$OUT/coldstart.txt"

say "Modern.Web (.NET 10), direct :5100"
for i in $(seq 1 "$REPEATS"); do
  pkill -f ConfReg.Modern.Web.dll 2>/dev/null || true; sleep 2
  nohup dotnet "$ROOT/src/Modern.Web/bin/Debug/net10.0/ConfReg.Modern.Web.dll" > "$LOGS/modern.log" 2>&1 &
  printf 'modern  run=%d  %s ms\n' "$i" "$(time_to_ready http://localhost:5100)" | tee -a "$OUT/coldstart.txt"
done

say "Legacy.Web (net48 on Mono + nginx, NOT IIS), direct :5080"
for i in $(seq 1 "$REPEATS"); do
  pkill -f fastcgi-mono-server4 2>/dev/null || true; sleep 2
  ( cd "$ROOT/src/Legacy.Web" && nohup fastcgi-mono-server4 /applications=/:"$PWD" /socket=tcp:127.0.0.1:9000 \
      /logfile=/tmp/confreg-fcgi.log > /tmp/confreg-fcgi.out 2>&1 & )
  printf 'legacy  run=%d  %s ms\n' "$i" "$(time_to_ready http://localhost:5080)" | tee -a "$OUT/coldstart.txt"
done

say "Gateway (YARP, .NET 10) :5000 - proxy only, backends already warm"
for i in $(seq 1 "$REPEATS"); do
  pkill -f ConfReg.Gateway.dll 2>/dev/null || true; sleep 2
  nohup dotnet "$ROOT/src/Gateway/bin/Debug/net10.0/ConfReg.Gateway.dll" > "$LOGS/gateway.log" 2>&1 &
  printf 'gateway run=%d  %s ms\n' "$i" "$(time_to_ready http://localhost:5000)" | tee -a "$OUT/coldstart.txt"
done

say "results: $OUT/coldstart.txt"
