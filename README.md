# Jelana for Jellyfin

A standalone, cache-first analytics plugin for Jellyfin 10.11.x.

Jelana records playback events itself. It does not require the standalone PHP
application or Playback Reporting. The UI only reads an atomically replaced
snapshot; it never runs analytics queries during a page request.

## Cache architecture

1. Playback events are appended to the plugin's own NDJSON event store.
2. Jellyfin's scheduled-task system builds a snapshot at startup and hourly.
3. A completed snapshot atomically replaces the previous file.
4. The authenticated API and the Statistics page only read that snapshot.

The last good snapshot remains available if a refresh fails.

## Build

```bash
dotnet build Jellyfin.Plugin.Jelana.sln --configuration Release
```

Install the resulting DLL in a `Jelana` directory below Jellyfin's plugin
directory, then restart Jellyfin.
