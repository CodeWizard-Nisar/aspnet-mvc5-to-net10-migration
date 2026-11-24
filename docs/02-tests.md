# 02 - Characterization tests

Phase 2 puts a safety net under the legacy app **before any of it is touched**. These tests do not say the
app is *right*. They say what it *does*, today, including the bugs. After every later phase they must pass
**unchanged**. If one needs editing to pass, behaviour changed, and that has to be a deliberate, reviewed decision.

## The design decision that matters: black-box, over HTTP

The obvious way to test a System.Web app is to new up controllers and fake `HttpContext`. I did not, for three reasons:

1. **It would have to change in Phase 3 and Phase 5.** Tests that reference `ConfReg.Legacy.*` types cannot
   survive the move to ASP.NET Core. A safety net that has to be rewritten during the migration is not a safety net.
2. **Faking `HttpContext.Current` is exactly the pain the migration removes.** `Server.MapPath`, `Session` and
   `Request.UserHostAddress` do not work outside a hosted app domain. Fighting that now is wasted effort.
3. **The thing that must not change is what a browser sees.** Redirect targets, status codes, JSON shape, cookies.

So the suite (`tests/Characterization`) is a `net10.0` xUnit project that **references no ConfReg assembly**.
It talks HTTP to `CONFREG_BASE_URL` and SQL to the database. The system under test is whatever you host:

| Phase | Point `CONFREG_BASE_URL` at |
|---|---|
| 1, 2, 3 | Legacy.Web |
| 5 | the YARP proxy fronting Legacy.Web and Modern.Web |
| 7 | the same proxy, with the Talks/Registrations context on EF Core |

The one place the tests must run code rather than make a request is the expiry job. That goes through a single
setting, `CONFREG_EXPIRY_CMD`, defaulting to `ConfReg.LegacyService.exe --once` (a hook added to the Windows
Service in this phase, the only production-code change made for the tests). In Phase 4 that one setting points at
`Jobs.Worker`; the tests do not change.

## What is covered (95 tests, 34 snapshot files)

| Area | Tests |
|---|---|
| **Snapshot of every GET action's JSON** (home, wizard step 1, login, dashboard, settings, attendees list/page 2/search/filtered, talks index/create/edit, registrations list/filtered/detail x3, confirmation, CSV export, 404s) | `ReadOnlySnapshotTests` |
| **Snapshot of every wizard step and every refusal** (invalid details, student without `.edu`, no ticket, full talk, overlap, six talks, step 4 review, declined card, confirmation, registration closed) | `WizardValidationSnapshotTests` |
| **Wizard end to end**: Standard $299 / Workshop $499 / Student $99 each to a reference code; step ordering; `.edu` rule (incl. `ox.ac.uk` rejected, case-insensitive); 5-talk limit; capacity; overlap vs merely-touching; attendee reuse; holds block other visitors; last seat; changing talks releases the old hold; declined card then retry | `WizardRulesTests` |
| **The 30-minute hold**: abandoned registration expires and frees seats; 29 min is left alone, 31 min expires; paid and cancelled untouched; job idempotent; paying beats the job | `ExpiryTests` |
| **Admin**: login (both roles, bad credentials), logout, cancel (frees seats, money does not move), refund (money returns and cancels), talks create/edit/capacity floor/delete rules, settings change prices and close registration, attendee filters, CSV (formula-injection guard), dashboard numbers and 80% warnings | `AdminBehaviourTests` |

"Snapshot of every controller action" covers GET actions as JSON. POST actions are covered by behaviour
assertions (redirect target, resulting database rows), and by snapshots of their validation responses.

### Why the snapshots are deterministic

The app seeds relative to "now", which would make every snapshot differ daily. So the tests do not use the
app's own seed. `tools/SeedSql` renders the **same `SeedPlan`** with a fixed clock (2026-11-20 12:00 UTC) into
`tests/Characterization/Data/seed.sql`. Before every test the fixture wipes all tables and reloads that script, with
the same ids every time. It deliberately loads *legacy-format* (AES) password ciphertext: after Phase 3 the app
must still log those users in, then re-hash them.

Anything that genuinely depends on the real clock is scrubbed (`Scrub.References`, `NowDates`, `Countdown`),
and nothing else. The "30 minutes later" scenarios back-date rows in SQL, because the app has no clock to move
(BASELINE E5).

Snapshots keep the **wire format exactly as sent** and only re-indent it: PascalCase names, `"\/Date(ms)\/"` dates,
decimals such as `37080.00`. That is the point: Phase 5's Newtonsoft settings have to reproduce these bytes.

## Known bugs, pinned as current behaviour

Tests named `..._KNOWN_BUG` assert behaviour that is **wrong**. They exist so a fix is a visible, reviewed change
to a test rather than something that happens by accident (and so a *migration* that "fixes" one by accident, or
re-creates one, is noticed).

| Test | The bug |
|---|---|
| `Admin_data_is_readable_without_login_KNOWN_BUG` (x6) | every admin `?format=json` response skips the layout where the only login check lives |
| `The_csv_export_is_downloadable_without_login_KNOWN_BUG` | a file result never renders the layout either |
| `Cancelling_a_registration_works_without_login_KNOWN_BUG` | admin POSTs redirect, so never reach the layout |
| `Staff_have_the_same_power_as_admins_KNOWN_BUG` | no role check anywhere |
| `Login_follows_an_unchecked_returnUrl_KNOWN_BUG` | open redirect |
| `Paying_after_the_job_expired_the_hold_resurrects_the_registration_KNOWN_BUG` | session hold and DB row disagree; a stale entity is re-attached and overwrites `Expired` with `Paid` while the seats may already belong to someone else |

**Phase 5 will intentionally break the first four** (cookie auth plus a global authorize filter). Those tests will be
updated in that phase, in a commit that says so, with the reason.

## Running it

**Windows (the real target):** host Legacy.Web in IIS Express or IIS, point it at a SQL Server (the compose file
gives you one), then:

```bash
cp .env.example .env && docker compose up -d
dotnet build ConfReg.sln
```

```bash
set CONFREG_BASE_URL=http://localhost:5080
dotnet test tests/Characterization
```

**Linux or CI, no Windows:** `tools/linux/host-legacy.sh` hosts the same legacy build on Mono (FastCGI behind nginx)
on port 5080, then `dotnet test tests/Characterization` as above. This is how Phase 2 was built and run.

| Variable | Default | Purpose |
|---|---|---|
| `CONFREG_BASE_URL` | `http://localhost:5080` | app under test |
| `CONFREG_TEST_SQL` | local SQL, `ConfReg` database, `sa` | **wiped and reloaded before every test; use a throwaway database** |
| `CONFREG_EXPIRY_CMD` | legacy service exe `--once` | how to run the expiry job once |
| `CONFREG_UPDATE_SNAPSHOTS` | unset | `1` rewrites snapshot files instead of comparing; review the diff |

If the app is not reachable the fixture **fails loudly**; it never skips.

## What was verified, and the honest caveat

| Verified | How |
|---|---|
| Suite passes: **95 / 95**, three consecutive runs, two of them in compare mode (so snapshots are deterministic) | `dotnet test`, 1 minute per run |
| The suite has teeth | changed `Registration.MaxTalks` 5 to 6: 5 tests failed. Changed the hold to 45 minutes: 2 expiry tests failed. Both reverted |
| Phase 1's open questions are answered | the legacy app **does** run: EF6 creates and seeds the database, all views render, the layout redirect fires, the Windows Service method runs |

**Caveat that matters: these snapshots were generated on Mono 6.8 with SQL Server 2025 on Linux, not on
.NET Framework 4.8 under IIS on Windows.** Mono reimplements `System.Web`, so a difference is possible (JSON date or
decimal formatting, culture, time zone, redirect `Location` form). Nothing I observed suggests one, but I could not
test it. **The first thing to do on a Windows machine is run the suite in compare mode.** If anything differs, that
is a finding to review (which of the two is the "real" legacy behaviour?), not something to paper over by regenerating.

## Findings from running Phase 1 for real

- The unauthenticated JSON hole in the baseline is real and live, not just a code-reading conclusion.
- Phase 1's two-clock hold race (E2) reproduces: the resurrected registration is now an asserted test.
- One of my own Phase 1 claims needed correcting by running it: the seed's 10 "pending" registrations are dated
  before the fixed anchor, so the real job expires them all. Tests account for that rather than hiding it.
