-- wrk reporter: exact percentiles, machine-readable, and a hard check that we measured SUCCESSFUL responses.
--
-- Benchmarking 401s is the classic way to produce an impressive, meaningless number: an unauthenticated admin
-- endpoint is very fast. So every non-200 is counted and reported, and bench.sh refuses a run that saw any.
local non200 = 0
local status_seen = {}

response = function(status, headers, body)
  if status ~= 200 then
    non200 = non200 + 1
    status_seen[status] = (status_seen[status] or 0) + 1
  end
end

done = function(summary, latency, requests)
  local errors = summary.errors.connect + summary.errors.read + summary.errors.write
               + summary.errors.status + summary.errors.timeout
  local seconds = summary.duration / 1000000
  local codes = ""
  for status, count in pairs(status_seen) do codes = codes .. status .. "x" .. count .. " " end
  -- latency is in microseconds; report milliseconds.
  io.write(string.format(
    "RESULT rps=%.1f requests=%d seconds=%.1f wrk_errors=%d non200=%d p50=%.2f p95=%.2f p99=%.2f max=%.2f codes=%s\n",
    summary.requests / seconds, summary.requests, seconds, errors, non200,
    latency:percentile(50) / 1000, latency:percentile(95) / 1000,
    latency:percentile(99) / 1000, latency.max / 1000,
    (codes == "" and "-" or codes)))
end
