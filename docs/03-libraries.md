# 03 - Breaking the web dependency, multi-targeting the libraries

Goal of the phase: `Shared.Domain` and `Shared.Data` compile for **both** `net48` and `net10.0` from one source
tree, with nothing in them that needs a web host or `Web.config`. After this phase the legacy app and any
.NET 10 app can run *identical* business logic, which is the precondition for moving pages one at a time.

Exit criteria, and how each was checked:

| Criterion | Evidence |
|---|---|
| Libraries build for net48 and net10.0 | `dotnet build ConfReg.sln`, 0 warnings, 0 errors |
| No `System.Web` / `ConfigurationManager` in them | the net10.0 build has neither; the one `ConfigurationManager` file is excluded from it by the project file |
| Legacy app still runs | restarted on the refactored code |
| **Phase 2 tests green, unchanged** | 95 original tests pass, no existing test or snapshot file modified (`git status` shows one new file) |
| Libraries genuinely run on .NET 10 | new `tests/Libraries.Net10`: 23 tests, EF6 creating, seeding and querying SQL Server **on .NET 10** |

## What replaced what

| Phase 1 | Phase 3 |
|---|---|
| `HttpContext.Current.Session` in `WizardSession` (#1) | `ISessionStore`; `WizardSession` is now an ordinary object that is handed one |
| `Request.UserHostAddress` in `PaymentGateway` (#1) | `IClientInfo` |
| `Server.MapPath` in `PaymentGateway` (#1) | `IAppPaths` |
| ~10 inline `ConfigurationManager.AppSettings` reads (#3) | `ISettings`, filled **once** at start-up |
| connection string, API key, AES key in `Web.config` (#4) | `secrets.config` (git-ignored) or `ConfReg__*` environment variables |
| `new ConfRegContext()` hard-wired to `name=ConfReg` | `IConfRegContextFactory`, connection string from `ISettings` |
| reversible AES "password hash" (#7) | BCrypt, upgraded on next login |
| leaked `SqlConnection` in `ReportQueries` (#9) | same SQL through the context; nothing left to forget to close |
| controllers `new` their own services | constructor injection from a composition root |

`ISessionStore`, `IClientInfo`, `IAppPaths` and `ISettings` live in `Shared.Domain/Abstractions`. The legacy
implementations (`HttpSessionStore`, `HttpClientInfo`, `ServerAppPaths`) live in `Legacy.Web/Infrastructure`, which is
the only place `System.Web` is still allowed. Portable ones (`DictionarySessionStore`, `StaticClientInfo`,
`DirectoryAppPaths`) ship in the library for workers and tests.

### Configuration: one source of names, two ways to load it

`ConfRegOptions` is a plain object (section `ConfReg`). Both hosts end up with the same `ConfRegSettings : ISettings`
over it; only the loading differs:

- **Legacy:** `AppConfigLoader` (net48 only) reads environment variable `ConfReg__Name`, then appSettings `ConfReg:Name`.
- **Modern:** `services.Configure<ConfRegOptions>(config.GetSection("ConfReg"))`.

So one set of environment variables (`ConfReg__ConnectionString`, `ConfReg__HoldMinutes`, ...) configures every host.
A missing secret fails loudly the first time it is used, with a message naming the variable to set; nothing falls back to
a placeholder.

**Secrets (#4).** `Web.config` no longer holds any. It pulls in `secrets.config` via `<appSettings file="...">`. Copy
`config/secrets.config.example` to `src/Legacy.Web/secrets.config` (git-ignored; Phase 3 also copied it for `Legacy.Service`, which Phase 4 retired in favour of `Jobs.Worker`, configured by user-secrets or `ConfReg__*` variables).
Every value in the example is a made-up demo placeholder. *The legacy host has no "user-secrets"* (that is a .NET Core
tool), so a git-ignored file plus environment variables is the honest equivalent here; the modern hosts will use real
user-secrets in Phases 4 and 5.

### Composition root, no DI container (deliberate)

`Legacy.Web/Infrastructure/LegacyComposition.cs` builds the whole object graph by hand and plugs into MVC as an
`IDependencyResolver`. A container would add assemblies, and on .NET Framework every added assembly is a potential binding
redirect, to a legacy app that gains nothing while the graph fits on one screen. The modern host gets
`Microsoft.Extensions.DependencyInjection` for free in Phase 5.

### Passwords (#7)

New databases seed BCrypt (work factor 11). Existing rows are legacy AES ciphertext and **cannot be bulk-converted**:
converting needs the plain-text password, which is exactly what we are not supposed to have. So
`AdminAuthService.Validate` checks a legacy row once with `LegacyPasswordCipher`, and on success immediately overwrites it
with a BCrypt hash. Each admin is upgraded the next time they log in. Properties, all tested:

- a wrong password upgrades nothing, and a garbage stored value is a failed login, never an exception;
- after upgrade the same password keeps working, and a later login does not rewrite the row;
- `LegacyPasswordCipher` is kept only for this and is to be deleted once no row lacks the `$2` prefix. Its comparison is
  now constant-time (Phase 1 used `==`).

## Why EF6 stays (a deliberate sequencing choice)

EF 6.5 runs on .NET 10, so the data layer changes **runtime** without being **rewritten**. The alternative, moving to
EF Core in the same step, changes two things at once: the runtime and the ORM's query translation, change tracking,
lazy loading and SQL generation. When a characterization test then fails you cannot tell which move broke it, and this
codebase leans on exactly the EF6 behaviours that differ most (lazy-loaded navigation properties on proxies, re-`Attach`
of detached entities in `RegistrationService.Pay`, `Include("...")` strings). So: runtime first, with every test green,
ORM later and bounded to one context (Phase 7), where the generated SQL can be compared before and after.

## Things this phase found that were not obvious

1. **EF6 on .NET uses `System.Data.SqlClient`, not `Microsoft.Data.SqlClient`.** A connection string produced by
   `Microsoft.Data.SqlClient`'s builder (`Trust Server Certificate=True`, with spaces) is rejected by EF6 with
   *"Keyword not supported"*. Write `TrustServerCertificate=True` by hand. It would bite the first time a Worker built its
   connection string with the "modern" builder.
2. **BCrypt drags four `System.*` helper assemblies into net48** (`System.Memory`, `System.Buffers`,
   `System.Numerics.Vectors`, `System.Runtime.CompilerServices.Unsafe`). .NET Framework does not unify them on its own.
   The exact binding redirects are in `Web.config` (the service's `.exe.config` gets them generated by the SDK).
3. **The library had to stop *constructing* its own context.** `base("name=ConfReg")` silently reads `Web.config`, which
   does not exist on .NET 10. The connection string is now passed in.
4. **A latent default bug disappeared by accident of making defaults explicit:** if a price row was missing, Phase 1
   defaulted *every* ticket type to 299. Defaults are now per ticket type (299 / 499 / 99). Unreachable in the tests (the
   rows always exist), covered by a new .NET 10 test.

## What this phase deliberately did NOT do

| Not done | Why / when |
|---|---|
| ASP.NET Core implementations of `ISessionStore` / `IClientInfo` / `IAppPaths` | There is no Modern.Web to consume them yet, so they would be untestable code. They arrive with it in **Phase 5**. Scope cut, stated rather than faked. |
| `WizardState` still holds whole EF entities | Phase 3 changes *where* session is reached from, not *what* is kept in it. This is the centrepiece of **Phase 5**. |
| No `IClock` | The clock is still `DateTime.UtcNow` (E5); tests back-date rows. Not required by this phase's goal. |
| `Global.asax` still exists | `HttpApplication` is how .NET Framework starts. It now only calls the composition root. Retired in Phase 5. |
| The Windows Service still exists (#8) | **Phase 4.** Its wiring changed only because the service it wraps now takes its dependencies. |
| E1 to E4 (capacity race, stale re-attach, session fixation, open redirect) | Pinned by Phase 2 tests; not in this phase's scope. |

## Verification, and what is not verified

- 95 Phase 2 tests + 3 new HTTP tests pass against the refactored legacy app (98/98): the original files are untouched.
- 23 tests run the net10.0 build of the libraries in-process against a real SQL Server (pure rules, BCrypt, legacy-cipher
  round trip, settings binding, EF6 create/seed, hold/pay/expire through the real services, the audit log picking up the
  injected IP and path, the legacy-to-BCrypt upgrade, and a connection-leak regression).
- **Not verified:** (a) running under **IIS on Windows**: the whole run is Mono on Linux, as in Phase 2; (b) the four
  binding redirects, which Mono does not need and IIS does, so they are correct by construction (versions read from the
  assemblies) but untested; (c) the leak regression test has not been run against the *old* code, so "it would have failed
  before" is reasoned, not demonstrated.
