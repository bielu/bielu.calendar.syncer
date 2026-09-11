# AGENT.md — bielu.calendar.syncer

This file describes the architecture, conventions, and coding principles for AI agents and developers working on this repository.

---

## Project Overview

`bielu.calendar.syncer` keeps an arbitrary number of calendars in sync by **mirroring events between every
connected account**. Connect two or more Google and/or Outlook calendars and an event created in any one of
them appears as a copy in all the others, staying in step as it is edited or deleted.

**Primary language:** C#
**Runtime target:** .NET 10 (modern, DI-first)
**Key integrations:** Google Calendar API v3, Microsoft Graph v1.0, ASP.NET Core minimal APIs, macOS launchd

---

## Repository Structure

```
/
├── .changeset/            # Changeset-driven versioning
├── .github/workflows/     # CI/CD pipelines
├── scripts/               # LaunchAgent installer, NuGet version projection
├── src/
│   ├── Bielu.Calendar.Syncer/            # Core abstractions, models, mirroring engine, stores
│   ├── Bielu.Calendar.Syncer.Google/     # Google Calendar provider
│   ├── Bielu.Calendar.Syncer.Microsoft/  # Microsoft Graph (Outlook) provider
│   ├── Bielu.Calendar.Syncer.Dashboard/  # Local web dashboard slice
│   ├── Bielu.Calendar.Syncer.Service/    # Host process (worker + dashboard)
│   ├── Bielu.Calendar.Syncer.Tests/      # Engine tests (loop prevention, deletion semantics)
│   ├── Bielu.Calendar.Syncer.slnx        # Solution
│   ├── Directory.Build.props
│   ├── Directory.Packages.props
│   └── nuget.config
├── AGENT.md
├── LICENSE
├── README.md
└── version.props
```

**The solution, both `Directory.*.props`, `nuget.config` and the test project all live inside `src/` — not at
the repository root.** Only `version.props` sits at the root, and `src/Directory.Build.props` imports it as
`../version.props`. This is not cosmetic: the shared CI templates in `bielu/bielu.GithubActions.Templates` run
MSBuild with `src` as the working directory, so a root-level solution is never found and the build fails with
`MSB1003`. Match the layout of `bielu.microservices.orchestrator` and `bielu.staticcode.analyzers` exactly.

---

## Core Design Principles

### KISS — Keep It Simple, Stupid

- The providers talk plain REST over `HttpClient` rather than pulling in the Google and Graph SDKs. The four
  operations needed (list a window, create, update, delete) do not justify two large dependency trees.
- State is two JSON files under `~/.bielu/calendar-syncer`. No database, no migration story.
- Default options work out of the box; only the OAuth client ids must be supplied.

### DRY — Don't Repeat Yourself

- The OAuth authorization-code-with-PKCE handshake lives once in `OAuth2Authenticator` in core. Providers
  supply endpoints, scopes and a display-name lookup — nothing else.
- HTTP plumbing (bearer auth, error bodies, missing-resource handling) lives once in `RestCalendarProvider`.
- Models (`CalendarEvent`, `CalendarAccount`, `SyncLink`, `SyncWindow`) are defined once in core.
- If you find yourself copying logic between providers, extract it into core.

### SOLID Principles

**Single Responsibility** — `ICalendarProvider` reads and writes events; `ICalendarAuthenticator` handles
sign-in; `IAccountManager` owns the account lifecycle; `ICalendarSyncer` owns mirroring. Do not merge them.

**Open/Closed** — A new calendar backend is a new slice implementing the existing interfaces. Core does not
change when one is added.

**Liskov Substitution** — A registered provider must fully implement its interface. If a backend cannot do
something, throw `NotSupportedException` rather than silently no-op.

**Interface Segregation** — Callers depend on the one interface they need. The dashboard uses `IAccountManager`
and `ISyncStatusTracker`; it never reaches into a provider.

**Dependency Inversion** — Everything resolves through `ICalendarProviderRegistry`. Never `new` a provider.

---

## Feature Slicing

Organised by **feature slice**, not technical layer. Each slice owns its abstractions, implementation, DI
registration, and configuration, and ships as a standalone NuGet package.

| Slice | Package | What it owns |
|---|---|---|
| Core | `Bielu.Calendar.Syncer` | Models, interfaces, mirroring engine, JSON stores, OAuth base, worker |
| Google | `Bielu.Calendar.Syncer.Google` | `GoogleCalendarProvider`, authenticator + `AddGoogleCalendar()` |
| Microsoft | `Bielu.Calendar.Syncer.Microsoft` | `MicrosoftCalendarProvider`, authenticator + `AddMicrosoftCalendar()` |
| Dashboard | `Bielu.Calendar.Syncer.Dashboard` | Minimal-API endpoints, status contracts, UI + `MapCalendarSyncerDashboard()` |

### Rules for feature slices

- **A slice is the unit of change.** Adding a calendar backend creates a new project; it does not touch existing slices.
- **Slices depend inward, never sideways.** `Google` and `Microsoft` both depend on core and never on each other.
- **Each slice registers itself** via its own extension method on `ICalendarSyncerBuilder`.
- **Keep slice boundaries hard.** If a provider needs a type from another provider, that type belongs in core.

---

## Key Interfaces

| Interface | Responsibility |
|---|---|
| `ICalendarSyncer` | Runs one mirroring pass across every pair of enabled accounts |
| `ICalendarProvider` | Reads and writes events for one calendar backend |
| `ICalendarAuthenticator` | Builds the consent URL, exchanges the code, refreshes access tokens |
| `IAccountManager` | Connect, pause, disconnect — including cleanup of mirrored copies |
| `IAccountStore` / `ISyncStateStore` | Persist connected accounts and source→mirror links |
| `ISyncStatusTracker` | In-memory run history for the dashboard |
| `ICalendarProviderRegistry` | Resolves provider and authenticator by provider name |

---

## How Mirroring Avoids Loops

This is the single most important invariant in the repository. Every mirrored event carries a
`MirrorMarker` — `[bielu-sync:<sourceAccountId>:<sourceEventId>]` — embedded in its body. The engine only
treats events **without** a marker as sync sources, so a copy is never copied onward.

The marker lives in the event body rather than only in local state, so mirrors stay recognisable even if
`links.json` is lost. `SyncLink` records additionally map a source event to its mirror on each target, which is
what makes updates and deletions resolvable.

Deletion is inferred from absence: a link whose source event was not returned **and** whose `SourceStart` falls
inside the sync window means the mirror is stale, either because the source event was deleted or because it was
moved out of range. Both cases warrant removing the mirror, which would otherwise sit at a time when nothing
happens. When `SourceStart` falls *outside* the window the source was never fetched, so absence tells us
nothing and the mirror is left alone. That window check is what stops events aging out of the look-behind
range from being mistaken for deletions — do not remove it. Both branches are covered by tests.

---

## Coding Conventions

### General

- `.editorconfig` sets `dotnet_analyzer_diagnostic.severity = error`. **Every analyzer diagnostic fails the
  build.** In practice this means: source-generated logging via `[LoggerMessage]` (CA1848), types owning a
  `SemaphoreSlim` implement `IDisposable` (CA1001), and no reserved words as interface parameter names (CA1716).
- Use `async`/`await` throughout. Never block with `.Result` or `.Wait()`.
- All public methods performing I/O return `Task`/`Task<T>` and accept a `CancellationToken`.
- Use primary constructors and records where they reduce boilerplate.
- Null-check at public API boundaries with `ArgumentNullException.ThrowIfNull`.
- Inject `TimeProvider` rather than reading `DateTimeOffset.UtcNow` directly.

### Naming

- Interfaces: `I` prefix, noun phrase (`ICalendarProvider`).
- Implementations: prefixed by the backend (`GoogleCalendarProvider`, `MicrosoftCalendarAuthenticator`).
- Extension method classes: `[Feature]Extensions` (`GoogleCalendarExtensions`, `DashboardEndpoints`).

### Dependency Injection

- `AddCalendarSyncer()` returns `ICalendarSyncerBuilder`; provider slices extend that builder.
- Providers register through `TryAddEnumerable`, so the registry sees every registered slice and a double
  registration is harmless.

### Error Handling

- A failure against one account must not abort the whole run. The engine collects per-account and per-pair
  errors into `SyncRunResult.Errors` and carries on.
- Deleting a mirror that is already gone is success, not an error — see `SendIgnoringMissingAsync`.

---

### Tests

`src/Bielu.Calendar.Syncer.Tests` covers the mirroring engine against in-memory fakes — no network, no files.
`FakeCalendarProvider` deliberately re-parses the mirror marker out of the event body on read, exactly as the
real providers do; without that the loop-prevention tests would pass vacuously. Any change to the engine must
keep `DoesNotMirrorAMirrorBackToItsOrigin` and `RepeatedRunsDoNotChurnUnchangedEvents` green.

---

## Adding a New Calendar Backend

1. Create `Bielu.Calendar.Syncer.<Name>`, referencing core only.
2. Implement `RestCalendarProvider` (or `ICalendarProvider` directly) and subclass `OAuth2Authenticator`.
3. Map the backend's fields onto `CalendarEvent`, including `IsPrivate` and `Availability`. Where the backend
   models fewer states than `EventAvailability`, collapse deliberately and comment why.
4. Add `Add<Name>Calendar(this ICalendarSyncerBuilder builder, ...)` registering both services plus a named
   `HttpClient`.
5. Register it in `Program.cs` and document the client-id setup in `README.md`.
6. Add a changeset.

---

## What Agents Should Not Do

- Do not remove the marker-based loop prevention or the sync-window check on deletions.
- Do not add provider-specific concepts (Graph ids, Google transparency values) to core.
- Do not log or serialise refresh tokens anywhere other than `accounts.json`, which is written `0600`.
- Do not widen the dashboard beyond loopback — it is unauthenticated by design because it only binds `127.0.0.1`.
- Do not suppress analyzer diagnostics to make a build pass; fix the code.
- Do not use `Thread.Sleep` or blocking calls in async paths.

---

## License

MIT — see `LICENSE`.
