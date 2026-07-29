# Jelana for Jellyfin

A standalone, cache-first analytics plugin for Jellyfin 10.11.x.

Jelana is independent from the standalone PHP application and uses Jellyfin's
Playback Reporting plugin as its history source. The UI only reads an atomically
replaced snapshot; it never runs analytics or Playback Reporting database
queries during a page request.

## Cache architecture

1. Jellyfin Playback Reporting records playback history.
2. Jellyfin's scheduled-task system reads that database in one bounded,
   read-only pass at startup and hourly.
3. A completed snapshot atomically replaces the previous file.
4. The authenticated API and the Statistics page only read that snapshot.

The last good snapshot remains available if a refresh fails.
The media codec and resolution profile has a separate six-hour cache, matching
the standalone Jelana implementation. Poster images use Jellyfin's own cached
image endpoint.

Playback counts use the same 30-minute session-gap CTE as Jelana. Playback
summaries, 7/30-day movie and series rankings, user rankings, daily activity,
playback methods and clients are calculated from Playback Reporting.

There is deliberately no HTTP endpoint that rebuilds statistics. Refreshes only
run through Jellyfin's scheduled-task system, ensuring that opening or reloading
the Statistics page cannot touch Playback Reporting or scan the media library.

## Requirements

- Jellyfin 10.11.x
- Playback Reporting plugin

## Build

```bash
dotnet build Jellyfin.Plugin.Jelana.sln --configuration Release
```

Install the resulting DLL in a `Jelana` directory below Jellyfin's plugin
directory, then restart Jellyfin.

## Menu link for regular users

Jellyfin does not expose server plugin pages in the regular user menu. With JS
Injector, Jelana can add **Analytics** for every signed-in user. Add this loader
to JS Injector:

```js
(() => {
    const loadJelanaMenu = () => {
        if (!window.ApiClient) {
            window.setTimeout(loadJelanaMenu, 500);
            return;
        }

        if (document.getElementById('jelana-menu-loader')) return;
        const script = document.createElement('script');
        script.id = 'jelana-menu-loader';
        script.src = ApiClient.getUrl('Jelana/Menu.js', { version: '0.1.17.0' });
        document.head.appendChild(script);
    };

    loadJelanaMenu();
})();
```

The link opens the authenticated user page at `/Jelana/User`. Menu behavior and
future compatibility fixes remain bundled in Jelana itself. Enable
**Requires Authentication** for this script in JS Injector.
