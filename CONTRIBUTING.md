# Contributing

Thanks for wanting to help. Bug reports, ideas and pull requests are all welcome.

## Build and test

You need Windows 10 or 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
git clone https://github.com/fotis89/pc-cluster
cd pc-cluster
dotnet test                              # unit tests
dotnet run --project src\Cluster         # run from source
dotnet publish src\Cluster -c Release -o out   # self-contained single-file exe in out\
```

`Cluster.exe --exit` asks a running copy to quit cleanly, which is handy between runs.

## Pull requests

- Keep changes focused; one topic per pull request.
- Add or update tests for logic in `Model/`, `Settings/` and placement math (`Ui/Monitors.cs`).
- Run `dotnet test` before pushing; CI runs it on every pull request.
- UI changes: include a before/after screenshot. To refresh the README images, run
  `$env:CLUSTER_DOCS = (Get-Location); dotnet test --filter DocsRender`.

## Ground rules for the app

These are promises in the README, so changes must keep them true:

- **Local only.** No network connections, telemetry, analytics, crash upload or auto-update.
- **No admin rights, drivers or services.** Writes only to `%LOCALAPPDATA%\Cluster` and the user's `Run` key.
- **Supported Windows APIs only.** No injecting into Explorer and no reparenting into the taskbar.
- **Light.** Sample once a second off the UI thread; draw only when something changes.

## Code style

Follow the existing style: file-scoped namespaces, `internal` by default, small focused classes, and comments only
where the *why* isn't obvious.
