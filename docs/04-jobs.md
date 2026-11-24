# 04 - Background jobs move to a .NET 10 Worker Service

Phase 4 retires the legacy Windows Service (BASELINE-PROBLEM #8) and replaces it with `src/Jobs.Worker`: a .NET 10
process running **Quartz.NET** on cron schedules, with **Serilog** structured logging and **health endpoints**.
It is the low-risk first runtime move, because it touches no user-facing page, and it is the first real proof that the
shared libraries run on .NET 10: the *same* `ExpiryService` that ran inside the Windows Service on .NET Framework now
runs inside the Worker, against the same database, and the Phase 2 tests cannot tell the difference.

## The three jobs

| Job | Schedule (cron, UTC) | What it does | Result line |
|---|---|---|---|
| `expire` | `0 * * * * ?` every minute | Pending registrations older than the hold (30 min) become Expired, which releases their seats | `expired=N` |
| `emails` | `0 0/5 * * * ?` every 5 min | one confirmation email per newly paid registration, exactly once | `sent=N` |
| `report` | `0 0 2 * * ?` 02:00 nightly | writes `App_Data/reports/capacity-YYYYMMDD.csv`: every talk with capacity, taken, remaining, percent full, and a warning flag above 80% | `report=PATH` |

Schedules are configuration (`Jobs:ExpireCron`, `Jobs:ConfirmationEmailCron`, `Jobs:CapacityReportCron`), tested to be
changeable without code.

The logic lives in `Shared.Data` (`ExpiryService`, `ConfirmationEmailService`, `CapacityReportService`); the Quartz job
classes are thin wrappers. So the same code runs from the scheduler, from the command line and from tests.

No real mail provider exists in this repo: `FileEmailSender` writes an `.eml` per message to `App_Data/outbox`. A real
sender is one class implementing `IEmailSender`, which must throw if a message was not accepted.

## What the Windows Service lacked, and what replaced it

| Windows Service (#8) | Worker |
|---|---|
| No overlap guard: a slow run plus the next tick meant two at once | `[DisallowConcurrentExecution]`: Quartz skips a trigger while the previous run is still going |
| `catch { EventLog.WriteEntry }`, error swallowed, service stays "Running" | failures are logged with the exception, recorded, and surface in `/health` |
| No health reporting | `/health/live` and `/health` |
| Plain-text logs in the Windows Event Log | Serilog: one compact JSON object per line (console + rolling file), text in Development |
| Windows only, second installer | runs anywhere .NET 10 runs; `UseWindowsService()` also lets it install as a Windows service (`sc create`) |
| Config in its own `App.config`, plain-text secrets | the same `ConfReg__*` names as the web app, via user-secrets or environment |
| Only the expiry job existed | all three |

## Design decisions worth reading

**Confirmation emails: idempotency is the problem, sending is easy.** The job runs every 5 minutes, can overlap a manual
run, and a mail provider can fail halfway through a batch. So each registration is *claimed* first with one atomic
statement, `UPDATE ... SET ConfirmationSentUtc = now WHERE Id = x AND ConfirmationSentUtc IS NULL`. Only the caller whose
UPDATE touches a row sends the mail. If sending then throws, the claim is released so the next run retries, and one bad
address never blocks the rest of the batch. Tested, including four workers racing over 25 registrations: every
confirmation is sent, each once.

**Quartz uses the in-memory store, deliberately.** Single instance, no scheduler tables to maintain. If you run two workers
each fires its own triggers. That is safe here by design (expiry is an idempotent UPDATE, emails are claimed atomically,
the report overwrites the same day's file), and wasteful rather than harmful. A clustered AdoJobStore is only worth it if
the jobs stop being idempotent.

**Misfire policy.** If the worker was down when a trigger was due: `expire` and `emails` do nothing and wait for the next
tick (it is a minute or five away); `report` fires once as soon as the worker is back, because a skipped night is a gap in
the record.

**Cron is evaluated in UTC**, not the server's local zone, so DST cannot move or double a run, and the schedule means the
same thing on every host.

**The worker never creates the database.** The legacy app creates and seeds it. A worker that did so would silently invent
an empty database in the wrong place if its connection string were mistyped, so its EF initializer is set to `null`.

**Health semantics.** `/health/live` never touches anything (is the process up?). `/health` checks the database, that
Quartz is running, and the last result of every job. A job whose most recent run failed makes it `Degraded` (HTTP 200, so a
single transient failure does not take the pod out of rotation; it is in the body and the logs). An unreachable database,
or a stopped scheduler, is `Unhealthy` (503).

## Running it

```bash
dotnet build ConfReg.sln
dotnet user-secrets set "ConfReg:ConnectionString" "Server=...;Database=ConfReg;...;TrustServerCertificate=True" --project src/Jobs.Worker
dotnet run --project src/Jobs.Worker              # the service; health on http://localhost:5090/health
```

Run one job once (for operators and the characterization tests). The result is the only thing on stdout; logs go to stderr:

```bash
dotnet src/Jobs.Worker/bin/Debug/net10.0/ConfReg.Jobs.Worker.dll --run-job expire     # expire | emails | report
```

Exit code 0 on success, 1 on failure or an unknown job name. A missing connection string fails at once with a message naming
the variable to set.

## How the old tests proved parity

The Phase 2 suite triggers the real expiry job through one harness setting. Phases 1 to 3 it ran the Windows Service exe with
`--once`; now `CONFREG_JOB_CMD` defaults to `dotnet ConfReg.Jobs.Worker.dll --run-job {job}`. That default, in
`Harness/TestEnvironment.cs` and `Harness/AppFixture.cs`, is the only thing that changed: **no Phase 2 test file was edited**
and all 95 still pass. Phase 4 added three HTTP-level tests (new file `BackgroundJobTests.cs`) for the two new jobs.

## Verification, and what is not verified

| Suite | Result |
|---|---|
| Characterization (HTTP, against the legacy app, jobs via the Worker) | **101 / 101**: 95 original + 3 password + 3 background-job |
| `Libraries.Net10` (services on .NET 10, real SQL Server) | **29 / 29** (6 new: send once, only paid, failed-send retry, 4-way race, header injection, report) |
| `Jobs.Worker.Tests` (worker hosted in-process) | **6 / 6** (liveness, readiness, 503 on dead database, cron expressions, config override, a Quartz-triggered run showing up in `/health`) |
| By hand | all three jobs via `--run-job`; the service on :5090 fired `expire` on the minute; `.eml` and CSV inspected |

Not verified: running as an actual **Windows service** (`UseWindowsService()` is wired but only exercised as a no-op on
Linux); a real **mail provider**; **time-zone or DST** behaviour (cron is UTC by construction, not tested across a DST
change); a **clustered** scheduler; and everything as always on Mono/Linux rather than IIS/Windows.
