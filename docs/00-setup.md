# 00 - Running ConfReg locally

Four processes, and only the first one is awkward:

| Process | Runtime | Port | Needs |
|---|---|---|---|
| SQL Server | container | 1433 | Docker Desktop |
| `Legacy.Web` | .NET Framework 4.8 | 5080 | **Windows + IIS Express or IIS** |
| `Modern.Web` | .NET 10 | 5100 | .NET 10 SDK |
| `Gateway` (YARP) | .NET 10 | 5000 | .NET 10 SDK |
| `Jobs.Worker` | .NET 10 | 5090 | .NET 10 SDK (optional) |

**Browse the gateway, `http://localhost:5000`.** It is the only address a user should use: it serves the public wizard
from the old app and the admin portal from the new one. Admin login: `admin@confreg.example` / `ConfReg-Demo-1!`.

`Legacy.Web` uses `System.Web`, which cannot run on Kestrel, in a Linux container, or on macOS. That is not a
limitation of this repo; it is why the migration in this repo exists.

## Fastest path: see it without IIS at all

The admin portal is the part that has been migrated, and it runs on .NET 10, so it needs no IIS. You lose only the
public registration wizard (which is still on the old stack).

```powershell
.\tools\windows\run-stack.ps1 -SkipLegacy
```

Or by hand:

```powershell
copy .env.example .env
```

```powershell
docker compose up -d
```

```powershell
dotnet build ConfReg.sln -c Release
```

```powershell
dotnet run -c Release --project tools\DbInit
```

`DbInit` creates the schema and the demo data (200 attendees, 20 talks, 150 registrations) using the same EF6
initializer the legacy app uses. It exists so that looking at the application does not depend on getting IIS working
first. Add `-- --recreate` to drop and reseed.

```powershell
$env:ConfReg__ConnectionString="Server=localhost,1433;Database=ConfReg;User Id=sa;Password=ConfReg_Dev_Passw0rd!;TrustServerCertificate=True"; $env:ConfReg__LegacyCryptoKey="Y29uZnJlZy1kZW1vLWtleS1OT1QtQS1TRUNSRVQtMDA="; $env:ConfReg__LegacyCryptoIv="Y29uZnJlZy1kZW1vLWl2IQ=="; $env:ConfReg__PaymentApiKey="sk_test_DEMO_not_a_real_key"; $env:ConfReg__RemoteAppApiKey="deadbeefdeadbeefdeadbeefdeadbeef"; dotnet run -c Release --project src\Modern.Web
```

```powershell
$env:Migration__Pages__Account="Modern"; dotnet run -c Release --project src\Gateway
```

Then open `http://localhost:5000/Admin`.

## Everything, including the public wizard

```powershell
.\tools\windows\run-stack.ps1
```

The only extra piece is `Legacy.Web` under IIS Express:

```powershell
& "$env:ProgramFiles\IIS Express\iisexpress.exe" /path:"<repo>\src\Legacy.Web" /port:5080
```

`Legacy.Web` also needs its own secrets file (the .NET 10 projects use environment variables or user-secrets instead):

```powershell
copy config\secrets.config.example src\Legacy.Web\secrets.config
```

With the legacy app running, start `Modern.Web` with `Auth__AcceptLegacySession=true` and leave the gateway's
`Account` page on Legacy, which is the normal mid-migration state: sign in on .NET Framework, browse admin pages on
.NET 10. See [05-strangler.md](05-strangler.md).

## Troubleshooting

### `ERR_CONNECTION_REFUSED` on http://localhost:5000, and the windows closed immediately

The apps died on startup. The usual cause, now fixed, was a **repository path containing a space** (such as
`D:\New folder\...`): PowerShell's `Start-Process -ArgumentList` does not quote its entries, so `dotnet` was handed a
truncated `--project D:\New` and exited instantly, leaving no visible error. `run-stack.ps1` now launches each app with
`-WorkingDirectory` instead, and waits for both to answer before printing any URL.

To see a startup error directly, run one in the foreground from the repository root (a relative path has no space):

```powershell
dotnet run -c Release --project src\Gateway
```

### `error MSB3027` / `MSB3021`: "The file is locked by: ConfReg.Gateway" (or ConfReg.Modern.Web)

The app is still running from a previous run, and a running .NET process holds its own assemblies open, so the build
cannot overwrite them. Nothing is wrong with the code; every project compiles. The accompanying flood of `MSB3026`
warnings is the same copy being retried ten times, not ten separate problems.

```powershell
.\tools\windows\run-stack.ps1 -Stop
```

or, without the script:

```powershell
Get-Process | Where-Object { $_.ProcessName -like 'ConfReg*' } | Stop-Process -Force
```

`run-stack.ps1` now stops previous instances before it builds, so this should not recur.

IIS Express does not need stopping for a rebuild: ASP.NET shadow-copies assemblies, so it does not lock
`src\Legacy.Web\bin`.

## IIS troubleshooting

### `HTTP 403.14 - Forbidden` on `/`, or `HTTP 404.0` on `/Home/Index`, with `Handler: StaticFile`

Both mean the same thing: **ASP.NET routing never ran**, so IIS looked for `Home\Index` as a file on disk, found
nothing, and answered from the static-file handler.

IIS registers `UrlRoutingModule-4.0` with `preCondition="managedHandler"`, so it runs only for requests already mapped
to a managed handler. An extensionless MVC URL is not, so routing is skipped and every URL is treated as a file.
`src/Legacy.Web/Web.config` therefore re-registers that module **without** the precondition. If you see these errors,
you are running a build from before that fix: pull, rebuild, and restart IIS Express.

Mono does not read `system.webServer` at all, which is why the Linux test host used throughout development served
these URLs happily and could never have caught this. It was found by running on real IIS Express.

### Other things worth checking

- **The app pool must be .NET 4 Integrated.** The handler registration in `Web.config` carries
  `preCondition="integratedMode,runtimeVersion4.0"`; if the pool is Classic or "No Managed Code", the mapping is
  skipped and you are back to `Handler: StaticFile`.
- **`src\Legacy.Web\bin` must exist.** Run `dotnet build ConfReg.sln -c Release` first. `Legacy.Web` writes to `bin\`
  for both configurations.
- **ASP.NET 4.8 must be registered with IIS.** On full IIS, enable *Internet Information Services → World Wide Web
  Services → Application Development Features → ASP.NET 4.8*.
- **Point IIS at `src\Legacy.Web`**, the folder containing `Web.config`, `Global.asax` and `bin`, not the repository root.

## Other notes

- **The first legacy request is slow** (Razor compiles on demand, and EF6 builds its model).
- **`Jobs.Worker`** is independent: `dotnet run -c Release --project src\Jobs.Worker`, health at
  `http://localhost:5090/health`. Run one job once with `--run-job expire|emails|report`.
- **Every secret in this repo is a made-up demo placeholder.** See [03-libraries.md](03-libraries.md) for how real ones
  are supplied.
- **Linux or macOS**: `Modern.Web`, `Gateway` and `Jobs.Worker` run normally. `Legacy.Web` can only be hosted via the
  Mono harness in [../tools/linux/README.md](../tools/linux/README.md), which is a test stand-in for IIS, not a
  deployment story.
