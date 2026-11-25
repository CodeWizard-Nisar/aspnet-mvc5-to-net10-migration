# 05 - Strangler-fig migration of the web tier

This is the phase the repository exists for. Both applications serve traffic at the same time, behind one proxy, and the
admin portal moves from .NET Framework 4.8 to .NET 10 **one page at a time**, while the public registration wizard keeps
running on the old stack. Moving a page across — or back — is a configuration change.

Everything below was run. The section "What was actually observed" lists the commands and results, and
"Problems this phase hit" lists the eight things that went wrong, because those are the useful part.

```
                         ┌──────────────────────────────┐
  browser ──────────────►│  Gateway (YARP)  :5000       │
                         │  per-page routing table      │
                         └───────┬──────────────┬───────┘
                   /Register/*   │              │   /Talks/*, /Attendees/*, ...
                   /Home, ...    │              │   (whatever has been cut over)
                                 ▼              ▼
              ┌──────────────────────────┐   ┌────────────────────────────┐
              │ Legacy.Web  :5080        │   │ Modern.Web  :5100          │
              │ .NET Framework 4.8       │◄──┤ .NET 10, ASP.NET Core MVC  │
              │ MVC 5, owns the session  │   │ reads the shared session   │
              └──────────────────────────┘   └────────────────────────────┘
                   shared session over HTTP (SystemWebAdapters)
```

## The routing table is the migration

`src/Gateway/MigrationRoutes.cs` lists every migratable page and the URLs it owns. Two properties matter:

1. **Anything not in the table goes to Legacy.Web**, through a catch-all route ordered last. A URL nobody has thought
   about keeps working on the old app. That default is the entire safety property of a strangler migration.
2. **Where a page lives is configuration**, so cut-over and rollback are the same mechanism:

```bash
dotnet run --project src/Gateway    # Migration:Pages:Talks=Modern  (or Legacy)
```

`GET /gateway/routes` answers "which app is serving this page right now", which is the first thing you want when the
wrong app answered.

## Shared session: the part most people get wrong

Legacy.Web owns the session. Modern.Web borrows it over HTTP through
`Microsoft.AspNetCore.SystemWebAdapters`. The framework side exposes an endpoint; the core side fetches from it.

**Framework side** (`src/Legacy.Web/Global.asax.cs`):

```csharp
this.AddSystemWebAdapters()
    .AddJsonSessionSerializer(o => { foreach (var key in SessionKeys.Shared) o.KnownKeys[key.Item1] = key.Item2; })
    .AddRemoteAppServer(o => o.ApiKey = AppConfigLoader.Get("RemoteAppApiKey"))
    .AddSessionServer();
```

**Core side** (`src/Modern.Web/Program.cs`):

```csharp
builder.Services.AddSystemWebAdapters()
    .AddJsonSessionSerializer(o => { foreach (var key in SessionKeys.Shared) o.KnownKeys[key.Item1] = key.Item2; })
    .AddRemoteAppClient(o => { o.RemoteAppUrl = ...; o.ApiKey = ...; })
    .AddSessionClient(o => o.CookieName = "ConfReg.Session");
```

Six things have to be right, and five of them fail *silently*:

| # | Requirement | What happens when it is wrong |
|---|---|---|
| 1 | **Every shared key is registered on BOTH sides, with the same type** | The value is skipped. No error, no warning: the other app simply never sees it. |
| 2 | **Whatever is stored is serialisable** | Only discovered when a key is registered: the serializer then has to walk the object. |
| 3 | **The cookie name is the LEGACY app's**, set on `AddSessionClient` | Every lookup returns a new, empty session. |
| 4 | **ASP.NET Core's own `AddSession` must NOT share that cookie name** | `Error unprotecting the session cookie`, and the shared session never arrives. |
| 5 | **The API keys match**, 32 hex characters | 401 from the session endpoint. |
| 6 | **Session middleware runs BEFORE authentication** | Session is null during authentication: nobody can sign in. |

### Requirement 1 is why `SessionKeys` exists

```csharp
public static readonly Tuple<string, Type>[] Shared =
{
    Tuple.Create(AdminUserId, typeof(int)),
    Tuple.Create(AdminEmail,  typeof(string)),
    Tuple.Create(AdminRole,   typeof(string)),
    Tuple.Create(Wizard,      typeof(WizardSnapshot)),
};
```

One list, in `Shared.Domain`, that both apps enumerate. A key registered on one side only is not an error, it is a
*silence*, so the registration must not be written out twice by hand. Add a key here and both apps pick it up.

### Requirement 2 is why `WizardState` had to die

Phase 1 kept whole EF entities in session (BASELINE-PROBLEM #2): `Attendee`, `Talk`, `Registration` — dynamic proxies
with navigation cycles. In-process session never serialises anything, so it worked. The moment session crosses a process
boundary, every value must become JSON.

`WizardSnapshot` replaces it: ids and primitives, no behaviour, no entities. The rules it follows are in its own doc
comment. The important one: **store ids, re-load rows**. A copy of a row in session is a second version of the truth, and
the expiry job only ever updates the first one.

That change also fixed known bug **E2**. Phase 1's `Pay()` re-attached the stale entity from session and wrote `Paid`
over whatever the row then said, so paying after the expiry job had released the seats resurrected the registration.
`Pay()` now treats the row as the truth and marks it paid in one conditional statement:

```sql
UPDATE Registrations SET Status = 1, ConfirmedUtc = @now WHERE Id = @id AND Status = 0
```

If no row matches, the hold is gone: the charge is refunded and the attendee is told, rather than being charged for a
seat somebody else now holds. The Phase 2 test that pinned the bug was deliberately reversed (see below).

### Requirement 6, stated plainly

```csharp
app.UseRouting();            // the adapter reads the ENDPOINT's metadata to decide whether to fetch session
app.UseSystemWebAdapters();  // session loaded and bridged here
app.UseAuthentication();     // ...so this can read it
app.UseAuthorization();
```

Fetching the session is a **network call to the legacy app**, so it is not done for every request: only endpoints that
ask, via `[Session]` or route metadata. While the login page is still on Legacy, authentication needs it on every admin
request, so the admin routes carry that metadata; once Account has moved, only the diagnostics page asks.

It is fetched **read-only** on purpose. Modern.Web never writes the legacy session, so it never takes the write lock on
the legacy side, and nothing it does can corrupt legacy state.

### Seeing it work

`GET /Admin/Diagnostics/Session` (Modern.Web only) prints what the modern app can see of the session the *legacy* app
wrote. Walking the public wizard on Legacy and then calling it returned:

```json
{"Source":"ConfReg.Modern.Web","HoldVisibleFromLegacySession":true,"ReferenceCode":"CR-4LG4RJ4D",
 "TicketType":2,"TotalAmount":499,"SelectedTalkCount":1,"Email":"cross@example.org"}
```

Same reference code, across two runtimes and two processes.

## Authentication during the transition

Legacy.Web proves a login with `Session["AdminUserId"]`; Modern.Web issues its own authentication cookie with a
`ClaimsPrincipal`. For pages to move *before* the login page does, Modern.Web can accept the legacy login:
`Auth:AcceptLegacySession=true` adds a scheme that turns those session values into a principal (read-only). A policy
scheme picks per request: own cookie if the browser has one, otherwise the legacy session.

Both apps answer an unauthenticated request **identically**, so a page can move without any client noticing:

| Request | Response |
|---|---|
| browser page | `302` to `/Account/Login?returnUrl=...` |
| JSON (`?format=json` or `Accept: application/json`) | `401`, no redirect |

**Sequencing rule: move Account LAST, roll it back FIRST.** Once Account is on Modern, an admin holds a cookie the
legacy app cannot read, so any admin page still on Legacy answers 401 to a signed-in admin. The gateway refuses that
combination at start-up rather than serving it:

```
Unsafe migration routing: Account is on Modern, but Talks is still on Legacy. A signed-in admin would hold a
Modern authentication cookie that the legacy app cannot read, so those pages would answer 401. ...
```

## Preserving the JSON wire format

The legacy app's JSON comes from `JavaScriptSerializer`. ASP.NET Core's defaults differ in ways that break callers
silently, so `Modern.Web/Infrastructure/LegacyJson.cs` sets every relevant option explicitly, and
`tests/Strangler.Tests` pins each rule:

| Rule | Legacy | Core default |
|---|---|---|
| Property names | `PascalCase` | camelCase |
| Dates | `"\/Date(1805187600000)\/"` | ISO 8601; Newtonsoft adds `+0000` |
| Integral decimals | `299` | Newtonsoft writes `299.0` |
| Scale that exists | `37080.00` | preserved |
| Enums | numbers | numbers |
| Nulls | written | written |

Two of those were found by the Phase 2 snapshots failing, not by reading documentation:

- **Dates gained an offset.** EF6 returns `DateTimeKind.Unspecified`, and for Unspecified or Local Newtonsoft appends the
  server's offset. `DateTimeZoneHandling.Utc` means "treat Unspecified as UTC, write no offset", which matches.
  My first attempt used `RoundtripKind` with a comment explaining why it was right. It was not.
- **`299` became `299.0`.** Newtonsoft writes an integral decimal with a decimal point so it round-trips as `decimal`.
  `LegacyDecimalConverter` writes the value's own `ToString(InvariantCulture)` instead.

Both would have shipped unnoticed without the snapshots, and both are exactly the class of bug that makes a front end
misbehave for reasons nobody can find.

## Deliberate behaviour changes, and the tests that were reversed

Five Phase 2 tests asserted behaviour that was **wrong**, pinned so that fixing it would be a visible decision. Phase 5
fixed it, so those assertions were deliberately reversed. Every change is in the diff of
`tests/Characterization/Tests/`, with a comment saying what changed and why:

| Was pinned as current behaviour | Now |
|---|---|
| `Admin_data_is_readable_without_login_KNOWN_BUG` (6 paths) | `Admin_data_is_not_readable_without_login`: 401 for JSON, 302 for a browser |
| `The_csv_export_is_downloadable_without_login_KNOWN_BUG` | refused |
| `Cancelling_a_registration_works_without_login_KNOWN_BUG` | refused, and the row is untouched |
| `Login_follows_an_unchecked_returnUrl_KNOWN_BUG` | `Login_only_follows_a_local_returnUrl` (E4 fixed) |
| `Paying_after_the_job_expired_the_hold_resurrects_the_registration_KNOWN_BUG` | refused, nothing charged, seats stay free (E2 fixed) |

The security fix (BASELINE-PROBLEM #6) was applied to the **legacy app too**, not only the modern one. During a strangler
migration pages move one at a time; securing only the migrated ones would leave the rest open and make "migrated" and
"secured" the same decision. `Legacy.Web/Infrastructure/AdminAuthorizeFilter.cs` is a global filter with
`[AllowAnonymous]` on the public controllers, which is why every topology below behaves the same.

The other 96 tests, and **all 34 snapshot files, are unchanged**. That is the claim of this phase: the admin portal runs
on a different runtime, a different web framework and a different serializer, and the bytes on the wire are the same.

## Problems this phase hit

Eight things went wrong. Six were my own mistakes; two are defects in other people's code.

1. **`SystemWebAdapters` 2.3.0 does not run on .NET 10 as shipped.** Its `CoreServices` package — including its own
   `net10.0` build — references `Microsoft.AspNetCore.Server.IIS.IIISEnvironmentFeature`, a type **.NET 10 removed**.
   It breaks twice: MVC enumerates every application part's types to find controllers (`ReflectionTypeLoadException` at
   start-up), and the package registers four services from an IIS-hosting helper, two of which touch the removed type.
   `Modern.Web/Program.cs` drops the adapter assemblies as application parts and removes those four registrations, both
   with a guard that **throws if the package's shape changes** rather than silently doing nothing. They populate
   `System.Web.Hosting.HostingEnvironment` from IIS, which is meaningless off IIS and which this app never reads.
   *This is the single thing most likely to block anyone attempting this migration on .NET 10 today.*
2. **Mono's `System.Web` is missing four members the adapter calls** (`HostingEnvironment.IsDevelopmentEnvironment`,
   `QueueBackgroundWorkItem`, `HttpRequest.ReadEntityBodyMode`, `HttpResponse.ClientDisconnectedToken`). Only relevant
   to the Linux test host, not to IIS. See "Honesty about the test host".
3. **Core's session stole the legacy cookie.** I gave `AddSession` the legacy cookie name, so Core's middleware tried to
   data-protect-unprotect a plain ASP.NET session id. Remote session replaces Core's session; they must not share a cookie.
4. **Authentication ran before the session was loaded**, so every cross-app login failed with a `NullReferenceException`
   swallowed into "not signed in" — no error, no redirect loop, just an admin who could not get in.
5. **A custom authentication scheme has no challenge behaviour.** My legacy-session handler returned a bare 401 to
   browsers instead of redirecting to the login page. Found by the characterization tests in the mixed topology.
6. **Validation messages came out in a different order.** MVC 5 yielded them in property-declaration order; ASP.NET Core
   yields them in binder-visit order. Harmless unless a client shows "the first error" — or a test compares the list.
   The modern controller now orders by the form type's own property order.
7. **Rolling a page back after Account had moved returned 401 to a signed-in admin.** Found by doing it. Now refused at
   start-up by the gateway, with the sequencing rule stated.
8. **A pre-existing defect in my own Phase 2 harness.** `Talks/Create` pre-fills `StartsUtc` with "today + 30 days", and
   `Scrub.NowDates` only covered ±1 day, so the snapshot failed one day after it was recorded. It failed **identically
   against the untouched legacy app**, which is how I know it was not a migration bug. Fixed with a precise
   `Scrub.FormDefaultDate` rather than by widening `NowDates`, which would have erased the seed's fixed dates that the
   snapshots should pin.

## What was actually observed

The whole stack on one machine: `tools/linux/host-stack.sh` (gateway :5000, Legacy :5080, Modern :5100).
The characterization suite was run **unchanged** against three topologies, all 101 tests, same 34 snapshot files:

| Topology | `Migration:Pages` | Result |
|---|---|---|
| **A** all legacy | everything Legacy (direct, :5080) | **101 / 101** |
| **B** mixed | Account + Dashboard + Settings on Legacy; Talks + Attendees + Registrations on Modern | **101 / 101** |
| **C** admin fully migrated | all admin pages Modern; public wizard still Legacy | **101 / 101** |

Topology B is the real test: the admin signs in on **.NET Framework 4.8** and is authorised on **.NET 10**, in the same
browser session, while the public wizard runs on the old app throughout.

Also observed by hand:

- the public wizard served by Legacy (`"Version":"legacy-net48"`) while `/Admin` was served by Modern;
- a legacy-written wizard hold read by Modern (the diagnostics output above);
- an anonymous browser refused by Modern (`401` JSON, `302` page) in every topology;
- `Talks` rolled back to Legacy with one setting, and Legacy serving it again (`X-AspNet-Version` header returns);
- the gateway refusing the unsafe Account-on-Modern combination.

Plus automated: `tests/Strangler.Tests` **20 / 20** (the JSON rules and the routing table, including rollback and the
refusal), `tests/Libraries.Net10` **29 / 29**, `tests/Jobs.Worker.Tests` **6 / 6**. Solution builds with 0 warnings.

## Honesty about the test host

Everything above ran with Legacy.Web hosted on **Mono + nginx on Linux**, not IIS on Windows. Mono's `System.Web` lacks
four members the real `SystemWebAdapters` framework package calls, so `tools/linux/host-legacy.sh` rewrites exactly
those four call sites, in the copy under `bin/`, to stand-ins (`tools/linux/MonoShims`, `tools/linux/PatchFrameworkServices`,
and `tools/linux/README.md`). Only host-lifetime plumbing is replaced: the package's own session, serialisation, locking
and API-key code runs unmodified, and the shims are never deployed.

So: the session-sharing **protocol** is genuinely exercised end to end, by the real package on both sides, over HTTP,
with serialisation and API keys. What is **not** verified is the IIS-hosted behaviour of the framework side — in
particular IIS's session locking under concurrent requests, and the four members the shims stand in for. On Windows,
drop the shims (they are not used) and run the same three topologies before trusting any of it in production.

## What this phase did not do

| Not done | Why |
|---|---|
| Move the public wizard to Modern.Web | The brief moves the admin portal; the wizard staying on legacy is what makes it a strangler migration rather than a cut-over. Its session hold is now shareable, so it *could* move. |
| Antiforgery tokens on the modern forms | The legacy app has none, and adding them would change the HTML the Phase 2 tests pin. `SameSite=Lax` cookies are a partial defence. Worth doing once the legacy app is gone; called out in [08-tradeoffs.md](08-tradeoffs.md). |
| Role checks | Still none, in either app (`Staff` can do what `Admin` can). The modern side now carries a role claim, so the filter is one policy away. |
| E1 (last-seat race) and E3 (session fixation) | E3 is gone on the modern side (a fresh cookie per sign-in) but remains in the legacy login. E1 needs a transaction or a unique constraint; it is a data-layer change, not a web-tier one. |
| Hot-reloading the routing table | A route change is a deployment decision; a restart leaves a record. |
| HTTPS, HSTS, production cookie policy | Single-machine demo. `CookieSecurePolicy.SameAsRequest` must become `Always` behind TLS. |
