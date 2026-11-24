# 01 - The baseline: ConfReg on the legacy stack

Phase 1 builds the application we are going to migrate. It is a small but real line-of-business app
(a conference registration system) written the way these apps are actually written: .NET Framework 4.8,
ASP.NET MVC 5, Entity Framework 6.5, `Global.asax`, `Web.config`, Session state, and a Windows Service.

It is **deliberately flawed**. The flaws are not decoration; each one is a specific obstacle the later
phases remove. Every one is marked in the code with a `BASELINE-PROBLEM #n` comment explaining why it
hurts and which phase fixes it:

```bash
grep -rn "BASELINE-PROBLEM #" src --include=*.cs --include=*.cshtml --include=*.config
```

## What the app does

**Public wizard** (`Legacy.Web/Controllers/RegisterController.cs`)

| Step | Rule enforced |
|---|---|
| 1 Attendee | name and a valid email required |
| 2 Ticket | Standard $299, Workshop $499, Student $99. Student needs an `.edu` email |
| 3 Talks | at most 5; a full talk cannot be picked; two talks whose times overlap cannot both be picked |
| 4 Review and pay | stubbed gateway (no real provider anywhere); card ending `0002` is declined |
| Confirmation | shows the reference code (`CR-XXXXXXXX`) |

**The 30 minute hold.** Choosing talks (step 3) writes a `Pending` registration plus `RegistrationTalk`
rows. Those rows count against capacity, so the seats are genuinely held. The wizard also remembers the
hold *in Session* (`WizardState.Registration`, `HoldExpiresUtc`). If payment is not completed in 30 minutes
the hold lapses and the seats are released. This is the hardest thing in the app to migrate, because the
hold lives in two places that do not know about each other:

- **Session** knows the attendee's in-flight state and the deadline the *attendee* sees.
- **The database row** is what the expiry job sees. The job cannot read anyone's session.

**Admin portal** (login required, see problem #6): dashboard, attendees (search, filter, CSV export),
talks (create, edit, delete, capacity), registrations (detail, cancel, refund), settings (prices, open/close dates).

**Expiry** runs as a Windows Service (problem #8) calling `ExpiryService.ExpireUnpaid()` every 60 seconds.
The other two jobs from the brief (confirmation emails, nightly capacity report) do **not** exist yet; they
arrive with the Worker Service in Phase 4. Only the expiry job is needed for the Phase 1 definition of done.

**Seed data** (`Shared.Data/Seed/SeedPlan.cs`): 200 attendees, 20 talks over 3 tracks, 150 registrations
(120 paid, 10 pending, 10 cancelled, 10 expired), 125 payments. Deterministic (`Random(42)`).

## The ten deliberate problems

### 1. Business logic calls `HttpContext.Current.Session` directly
`Shared.Domain/Wizard/WizardSession.cs`, also `PaymentGateway.cs` (client IP, `Server.MapPath`).
The "domain" assembly references `System.Web`. It cannot load on .NET 10, cannot run in a worker or the
Windows Service (no `HttpContext.Current`, so it is `null`), and cannot be unit tested. **Fixed in Phase 3:**
`ISessionStore`, `IClientInfo`, `IAppPaths`.

### 2. The wizard stores whole EF entities in Session
`Shared.Domain/Wizard/WizardState.cs`. Entities are dynamic proxies with navigation cycles. They cannot be
serialised. InProc Session never serialises, so it "works", until Session is shared across processes in
Phase 5, where every value must be serialised. **Fixed in Phase 5:** a flat snapshot of ids and primitives.

### 3. `ConfigurationManager.AppSettings` scattered through the code
`HoldPolicy`, `PaymentGateway`, `PasswordCrypto`, `SettingsService`, the Windows Service. About ten reads, each
inline with its own default. **Fixed in Phase 3:** `ISettings`, backed by `IOptions` on .NET 10.

### 4. Secrets in plain text in `Web.config`
Connection string with password, the payment API key, the AES key and IV. Copied into `Legacy.Service/App.config`
as well. **All values in this repo are made-up, throwaway demo values** (the SQL one matches `.env.example`).
The *pattern* is the problem: committed, copied to servers, readable by anyone who can read the site folder.
**Fixed in Phase 3:** user-secrets and `.env`, nothing sensitive in the repo.

### 5. `Global.asax` for startup wiring
`Legacy.Web/Global.asax.cs`: database initializer, routes, no DI container, controllers `new` their services.
`HttpApplication` does not exist on ASP.NET Core. **Replaced in Phase 5** by `Modern.Web/Program.cs`.

### 6. No `[Authorize]`; the admin check is a `Response.Redirect` in `_AdminLayout.cshtml`
This one is worse than it sounds, and is **the first real finding of the characterization tests**. A layout only
runs when a *view* renders, so:

- every admin **POST** that redirects (Cancel, Refund, Delete, Save settings) runs with **no login check at all**;
- every `?format=json` response skips the layout, so **admin data is readable without logging in**;
- the CSV export (a file result) is likewise unprotected;
- there is no role check, so "Staff" can do what "Admin" can.

Phase 2 records this as *current behaviour*. Phase 5 (cookie auth + global authorize filter) deliberately
changes it, and that change will be called out as an intended behaviour change, not hidden in a green test run.

### 7. Admin passwords stored with reversible encryption
`PasswordCrypto` (AES-CBC, one fixed IV, key from `Web.config`). The column is *named* `PasswordHash` and holds
ciphertext. `AdminAuthService` "verifies" by decrypting and comparing. **Fixed in Phase 3:** BCrypt, upgrading each
row on that user's next successful login (a bulk conversion is impossible without the plain text).

### 8. A Windows Service wrapping one business-logic method
`Legacy.Service`: a second deployable, Windows-only, no overlap guard, no retries, swallowed errors. It is a
timer around `ExpiryService.ExpireUnpaid()`. **Replaced in Phase 4** by a Quartz.NET Worker Service.

### 9. A data-access class that opens a connection and never closes it
`Shared.Data/Services/ReportQueries.cs`: connection, command and reader are never disposed. It is called on every
dashboard load. After roughly 100 loads the pool is exhausted ("max pool size was reached"), and the stack trace
points at the caller, not here. **Fixed in Phase 3.**

### 10. No tests
Intentional. **Phase 2 fixes this before anything else is touched.**

## Unplanned problems (found while writing it, documented rather than hidden)

These are not in the brief's list. They appeared naturally and are real, so they are recorded here instead of
being quietly fixed, because pretending the baseline was cleaner than it was would make the write-up dishonest.

| # | Problem | Where |
|---|---|---|
| E1 | Check-then-insert capacity race: the last seat can be sold twice (no lock or transaction) | `RegistrationService.PlaceHold` |
| E2 | `Pay` re-`Attach`es a stale detached entity from Session and writes `Paid` over whatever the DB now says, so a registration the expiry job already expired (and whose seats were released) can be resurrected | `RegistrationService.Pay` |
| E3 | Session id not regenerated at login (session fixation) | `AccountController.Login` |
| E4 | `returnUrl` followed unchecked (open redirect) | `AccountController.Login` |
| E5 | No clock abstraction: `DateTime.UtcNow` inline, so tests must back-date rows rather than move time | `ExpiryService`, `RegistrationService` |
| E6 | JSON is produced by `JavaScriptSerializer`: PascalCase and dates as `\/Date(ms)\/`. ASP.NET Core does neither by default, so Phase 5 must reproduce both exactly | `AppController.Respond` |
| E7 | A new `DbContext` per call, no DI, services created with `new` | everywhere |

E2 is the most instructive: it is a direct consequence of keeping whole entities in Session (#2) plus two
independent clocks for the hold.

## Additions to the spec

Two things the brief's entity list did not cover but the features need:

- `Setting` (Key, Value): the admin Settings page edits prices and the registration window, so they need a table.
  Web.config values are the fallback when a row does not exist.
- `Registration.ConfirmationSentUtc`: the Phase 4 confirmation-email job must be idempotent, so it needs to know
  which paid registrations it has already mailed.

## Layout notes

- `Legacy.Service` lives at `src/Legacy.Service` (not in the brief's layout) because it is a real, separate
  legacy project. It is deleted in Phase 4 when `Jobs.Worker` replaces it.
- `Shared.Domain` and `Shared.Data` are `net48` only for now. Multi-targeting is Phase 3.
- Business logic that needs the database (`RegistrationService`, `ExpiryService`) is in `Shared.Data`, because
  `Shared.Data` references `Shared.Domain`, not the other way round. `Shared.Domain` holds entities, pure rules
  and the web-coupled pieces.
- The projects are SDK-style so everything builds with `dotnet build`, including on Linux CI. Classic System.Web
  projects normally use the older project format; see the note in `Legacy.Web.csproj`.

## Running it (Windows)

```bash
cp .env.example .env
docker compose up -d
dotnet build ConfReg.sln
```

Then publish `src/Legacy.Web` (the `bin\`, `Views\`, `Web.config` and `Global.asax` it produces) as an IIS or
IIS Express site. The first request creates and seeds the `ConfReg` database. To run the expiry service in a
terminal: `src\Legacy.Service\bin\Debug\net48\ConfReg.LegacyService.exe --console`.

Demo admin logins (seeded, made-up): `admin@confreg.example` / `ConfReg-Demo-1!` and
`staff@confreg.example` / `ConfReg-Demo-2!`.

Definition of done, by hand: register at `/Register/Step1` through to a reference code; log in at
`/Account/Login`, open the registration under `/Registrations`, cancel it; back-date a `Pending` row's
`CreatedUtc` by 31 minutes and watch the service flip it to `Expired` and free its seats.

## What was and was not verified in Phase 1

Phase 1 was written without being able to run it. **Phase 2 then ran it for real** (Mono 6.8 + SQL Server 2025 on
Linux; see [02-tests.md](02-tests.md)) and the open questions came back well:

| Was unverified in Phase 1 | Now |
|---|---|
| EF6 creating and seeding the database | works, on first request |
| Razor views compiling against their models | all render |
| Behaviour of every controller action | pinned by 95 characterization tests |

Still **not** verified: running under IIS on .NET Framework on Windows. Mono is a faithful but different
implementation of System.Web, so the first Windows run of the suite is the real confirmation.

What Phase 1 itself verified, without infrastructure: all four projects compile against net48 reference assemblies
with 0 warnings; the seed data satisfies every rule (200/20/3/150, no overbooked talk, no overlapping picks, no
student without `.edu`, 1 full talk, 3 above 80%); all 21 Razor views parse.
