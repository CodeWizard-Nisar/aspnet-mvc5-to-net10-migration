# aspnet-mvc5-to-net10-migration

Incremental migration of a legacy ASP.NET MVC 5 application to .NET 10,
without a rewrite and without downtime. Both versions run side by side
behind a proxy while pages move across one at a time.

![Architecture](docs/architecture.svg)

## Why this exists

.NET 8 reached end of support on 10 November 2026. A lot of production
systems are now running unsupported, and most of them are too large to
rewrite. The usual advice — "rewrite it in .NET Core" — is not advice a
business can act on when the application is earning money every day.

This repo demonstrates the alternative: a strangler-fig migration, where
the old and new applications serve traffic at the same time and
functionality moves across incrementally.

## What it demonstrates

- Multi-targeting shared libraries to `net48` and `net10.0` so both
  applications run the same business logic during the transition
- Removing `System.Web` dependencies from business logic behind
  interfaces, so the same code runs under both runtimes
- YARP and `Microsoft.AspNetCore.SystemWebAdapters` sharing session
  state between a .NET Framework app and a .NET 10 app
- Migrating background jobs to a Worker Service as the low-risk first step
- Characterization tests written before any change, as the safety net
- Why EF6 stays in place through the migration, and how one bounded
  context moves to EF Core afterwards

## The sample application

ConfReg, a conference registration system: a four-step public
registration wizard, an admin portal, and three background jobs.
Small enough to read in an afternoon, realistic enough to contain the
problems that make real migrations hard — session-held state, a
30-minute payment hold, capacity rules, and configuration scattered
through the code.

## The starting point

The legacy app is deliberately built with the problems these projects
actually have:

| Problem | Where |
|---|---|
| `HttpContext.Current` inside business logic | `Shared.Domain` |
| Whole EF entities stored in Session | Registration wizard |
| `ConfigurationManager` reads scattered throughout | Everywhere |
| Secrets in plain text in Web.config | `Legacy.Web/Web.config` |
| No `[Authorize]`; login check in `_Layout.cshtml` | Admin portal |
| Reversible password encryption | `AdminUser` |
| Connections opened and never closed | `Shared.Data` |
| No tests at all | — |

Each is documented in [docs/01-baseline.md](docs/01-baseline.md).

## How it was done

| Phase | What | Docs |
|---|---|---|
| 1 | Baseline legacy application | [01-baseline](docs/01-baseline.md) |
| 2 | Characterization tests | [02-tests](docs/02-tests.md) |
| 3 | Multi-target the libraries, remove System.Web | [03-libraries](docs/03-libraries.md) |
| 4 | Background jobs to a Worker Service | [04-jobs](docs/04-jobs.md) |
| 5 | Strangler-fig migration of the web tier | [05-strangler](docs/05-strangler.md) |
| 6 | Benchmarks | [06-results](docs/06-results.md) |
| 7 | EF6 to EF Core, one context | [07-efcore](docs/07-efcore.md) |

## Results

[Benchmark table goes here once Phase 6 is done — requests/sec, p95
latency, memory, cold start. Real measured numbers only.]

## Running it locally

```bash
docker compose up -d        # SQL Server
dotnet run --project src/Modern.Web
# Legacy.Web requires Windows and IIS Express
```

Full setup in [docs/00-setup.md](docs/00-setup.md).

## Trade-offs

Decisions made here that would be made differently elsewhere, and why:
EF6 retained through the migration; IIS on Windows rather than
containers; the front end left untouched.
See [docs/08-tradeoffs.md](docs/08-tradeoffs.md).
