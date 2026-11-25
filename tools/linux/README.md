# tools/linux — hosting the legacy app without Windows

`Legacy.Web` targets .NET Framework 4.8 and is meant to run on IIS. These scripts host it on **Mono + nginx** instead, so
the characterization suite can run on Linux and in CI. **This is a test harness, not a deployment story.**

| Script | What it does |
|---|---|
| `host-legacy.sh` | Builds and hosts Legacy.Web on `http://localhost:5080` (FastCGI behind nginx) |
| `host-stack.sh` | The whole Phase 5 stack: gateway `:5000`, Legacy `:5080`, Modern.Web `:5100` |

```bash
cp .env.example .env && docker compose up -d     # SQL Server
tools/linux/host-stack.sh                        # then talk to http://localhost:5000
tools/linux/host-stack.sh stop
```

Useful variables: `MIGRATION_PAGES='Talks=Modern;Dashboard=Legacy'` moves individual pages;
`AUTH_ACCEPT_LEGACY_SESSION=true` lets Modern.Web accept a login made on Legacy.Web.

## The Mono shims, and exactly what they change

`Microsoft.AspNetCore.SystemWebAdapters.FrameworkServices` (the real package, used unmodified on Windows) calls four
`System.Web` members that exist on .NET Framework under IIS but **not in Mono's reimplementation**:

| Member | Why it is missing | Stand-in |
|---|---|---|
| `HostingEnvironment.IsDevelopmentEnvironment` | .NET 4.x addition Mono never implemented | `false` |
| `HostingEnvironment.QueueBackgroundWorkItem` | IIS-managed background work | run on the thread pool |
| `HttpRequest.ReadEntityBodyMode` | .NET 4.5 addition | `Classic` |
| `HttpResponse.ClientDisconnectedToken` | .NET 4.6 addition | a token that never cancels |

`MonoShims` holds the stand-ins; `PatchFrameworkServices` rewrites exactly those four call sites with Mono.Cecil, in the
**copy under `src/Legacy.Web/bin/`**, after every build. `host-legacy.sh` does this automatically. Nothing in the
repository's source is patched, nothing is committed, and the shims are never deployed.

Only host-lifetime plumbing is replaced. The package's session serialisation, locking and API-key handling run
unmodified, so the session-sharing protocol the Phase 5 docs describe is genuinely exercised.

**On Windows, none of this is used.** Publish `src/Legacy.Web` to IIS or IIS Express and point `CONFREG_BASE_URL` at it.
What the shims stand in for is therefore untested on IIS, in particular session locking under concurrent requests — see
the honesty section in [../docs/05-strangler.md](../docs/05-strangler.md).
