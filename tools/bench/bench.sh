#!/usr/bin/env bash
# Phase 6 measurement harness. Produces the numbers in docs/06-results.md. Nothing here is estimated.
#
#   tools/bench/bench.sh            full run
#   tools/bench/bench.sh quick      shorter durations, for checking the harness itself
#
# Method, stated so the numbers can be judged:
#  - CLOSED LOOP: wrk holds a fixed number of concurrent connections and measures service time per request. These are
#    "p95 at N concurrent clients", not a fixed-rate test, so they do not suffer coordinated omission; equally they do
#    not tell you the latency at some target throughput.
#  - IDENTICAL DATA: the database is reset to the fixed Phase 2 seed ONCE, before anything is measured. Every measured
#    endpoint is a read-only GET, so all targets see byte-identical data. Row counts are checked afterwards to prove it.
#  - Each target is warmed up before measurement (JIT, connection pools, EF model build, Razor compilation on Mono).
#  - ROUND-ROBIN ORDER: every repeat measures all targets for a path back to back, so warm-up and drift spread across
#    targets instead of attaching to whichever ran first. (First attempt ran each target as a block, and the gateway
#    came out FASTER than talking to the same app directly - an impossibility that was pure ordering artifact.)
#  - Each case is run REPEATS times; the report keeps every run so spread is visible, not just a best number.
#  - The load generator shares 4 cores with both web apps, the gateway and SQL Server, so absolute throughput is a
#    property of this box. Comparisons measured in the same round are the meaningful part.
#  - Every non-200 response fails the run (see wrk-report.lua).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$ROOT/tools/bench/results"
CONFIG="${CONFIG:-Debug}"
mkdir -p "$OUT"

MODE="${1:-full}"
if [ "$MODE" = "quick" ]; then DURATION=5; WARMUP=3; REPEATS=2; else DURATION=15; WARMUP=5; REPEATS=5; fi
SETTLE="${SETTLE:-2}"   # idle seconds before each measurement, so the previous case stops interfering
CONNS="${CONNS:-8}"     # modest: the load generator, both web apps, the gateway and SQL Server share 4 cores
THREADS=2

LEGACY=http://localhost:5080
MODERN=http://localhost:5100
GATEWAY=http://localhost:5000
SQL="${CONFREG_TEST_SQL:-Server=localhost,1433;Database=ConfReg;User Id=sa;Password=ConfReg_Dev_Passw0rd!;TrustServerCertificate=True}"

say() { printf '\n== %s\n' "$*"; }

# ---- identical data -------------------------------------------------------------------------------------------------
say "resetting the database to the fixed Phase 2 seed"
dotnet "$ROOT/tools/bench/DbTool/bin/$CONFIG/net10.0/DbTool.dll" reset "$ROOT/tests/Characterization/Data/seed.sql"
BEFORE=$(dotnet "$ROOT/tools/bench/DbTool/bin/$CONFIG/net10.0/DbTool.dll" fingerprint)
echo "fingerprint before: $BEFORE"

# ---- logins ---------------------------------------------------------------------------------------------------------
# Admin endpoints require a signed-in admin. Done once, before measuring, so the password upgrade to BCrypt that happens
# on first login is NOT inside any measured window.
cookie_for() {
  local base="$1" jar; jar=$(mktemp)
  curl -fsS -c "$jar" -b "$jar" -o /dev/null \
    --data "Email=admin@confreg.example&Password=ConfReg-Demo-1!" "$base/Account/Login"
  # curl writes HttpOnly cookies with a "#HttpOnly_" prefix, which both the ASP.NET session cookie and the modern auth
  # cookie carry. Skipping every line that starts with "#" therefore skips exactly the cookies we need.
  sed 's/^#HttpOnly_//' "$jar" | awk 'NF && $1 !~ /^#/ {printf "%s=%s; ", $6, $7}'
}
LEGACY_COOKIE=$(cookie_for "$LEGACY")
MODERN_COOKIE=$(cookie_for "$MODERN")
GATEWAY_COOKIE=$(cookie_for "$GATEWAY")
[ -n "$LEGACY_COOKIE" ] && [ -n "$MODERN_COOKIE" ] && [ -n "$GATEWAY_COOKIE" ] || { echo "login failed; cannot measure" >&2; exit 1; }

# ---- one measurement ------------------------------------------------------------------------------------------------
# measure <label> <base-url> <cookie> <path> <run-number>
measure() {
  local label="$1" base="$2" cookie="$3" path="$4" run="$5"
  local url="$base$path"

  # Precondition: this exact request must return 200 with this cookie, or the measurement is meaningless.
  local code; code=$(curl -s -o /dev/null -w '%{http_code}' -H "Cookie: $cookie" "$url")
  [ "$code" = "200" ] || { echo "FAIL $label $path: precondition failed (HTTP $code)" | tee -a "$OUT/raw.txt"; return 1; }

  sleep "$SETTLE"
  wrk -t"$THREADS" -c"$CONNS" -d"${WARMUP}s" -H "Cookie: $cookie" "$url" >/dev/null 2>&1 || true
  local line
  line=$(wrk -t"$THREADS" -c"$CONNS" -d"${DURATION}s" -H "Cookie: $cookie" \
              -s "$ROOT/tools/bench/wrk-report.lua" "$url" 2>/dev/null | grep '^RESULT' || true)
  [ -n "$line" ] || { echo "FAIL $label $path run $run: wrk produced no result" | tee -a "$OUT/raw.txt"; return 1; }
  case "$line" in *"non200=0"*) ;; *) echo "FAIL $label $path run $run: non-200 responses -> $line" | tee -a "$OUT/raw.txt"; return 1;; esac
  printf '%-22s %-36s run=%d %s\n' "$label" "$path" "$run" "$line" | tee -a "$OUT/raw.txt"
}

# ---- rss <process-pattern> ------------------------------------------------------------------------------------------
rss_mb() {
  local pid; pid=$(pgrep -f "$1" | head -1)
  [ -n "$pid" ] && awk '/VmRSS/ {printf "%.0f", $2/1024}' "/proc/$pid/status" || echo "-"
}

: > "$OUT/raw.txt"
echo "mode=$MODE duration=${DURATION}s warmup=${WARMUP}s repeats=$REPEATS connections=$CONNS threads=$THREADS" | tee -a "$OUT/raw.txt"
echo "host: $(nproc) cores, $(free -m | awk '/^Mem:/{print $2"MB"}'), $(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2 | xargs)" | tee -a "$OUT/raw.txt"

# Endpoints that exist in BOTH apps, so legacy and modern can be compared on the same work.
PATHS=(
  "/Admin?format=json"                      # dashboard: several queries incl. a raw-SQL revenue roll-up
  "/Talks?format=json"                      # 20 rows + seat counts (group-by over the join table)
  "/Attendees?format=json"                  # paged list, 25 of 200, correlated sub-select per row
  "/Registrations/Details/1?format=json"    # single row with three Includes
)

say "throughput and latency: legacy (Mono) vs modern (.NET 10) vs modern through the gateway"
echo "   targets measured round-robin per path, $REPEATS repeats of ${DURATION}s at $CONNS connections" | tee -a "$OUT/raw.txt"
for run in $(seq 1 "$REPEATS"); do
  for p in "${PATHS[@]}"; do
    measure "legacy-mono-direct"  "$LEGACY"  "$LEGACY_COOKIE"  "$p" "$run" || true
    measure "modern-direct"       "$MODERN"  "$MODERN_COOKIE"  "$p" "$run" || true
    measure "modern-via-gateway"  "$GATEWAY" "$GATEWAY_COOKIE" "$p" "$run" || true
  done
done

say "memory after load (resident set size)"
{
  printf 'RSS legacy  (fastcgi-mono-server4) : %s MB\n' "$(rss_mb fastcgi-mono-server4)"
  printf 'RSS modern  (ConfReg.Modern.Web)   : %s MB\n' "$(rss_mb ConfReg.Modern.Web.dll)"
  printf 'RSS gateway (ConfReg.Gateway)      : %s MB\n' "$(rss_mb ConfReg.Gateway.dll)"
} | tee -a "$OUT/raw.txt"

# ---- data really was identical --------------------------------------------------------------------------------------
AFTER=$(dotnet "$ROOT/tools/bench/DbTool/bin/$CONFIG/net10.0/DbTool.dll" fingerprint)
echo "fingerprint after:  $AFTER" | tee -a "$OUT/raw.txt"
if [ "$BEFORE" = "$AFTER" ]; then echo "DATA UNCHANGED: every target measured the same rows" | tee -a "$OUT/raw.txt"
else echo "WARNING: the database changed during the run; throughput numbers are not comparable" | tee -a "$OUT/raw.txt"; fi

say "raw results: $OUT/raw.txt"
