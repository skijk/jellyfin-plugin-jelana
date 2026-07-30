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

Trending is intentionally different from Most played. It requires at least two
unique viewers and ranks titles primarily by viewer count. Active days and
week-over-week growth only break ties, and series activity is capped per viewer
and day so one person's episode binge cannot dominate the list. Most played
rankings remain volume-based.

There is deliberately no HTTP endpoint that rebuilds statistics. Refreshes only
run through Jellyfin's scheduled-task system, ensuring that opening or reloading
the Statistics page cannot touch Playback Reporting or scan the media library.

## Dependencies

| Component | Status | Used for |
| --- | --- | --- |
| Jellyfin Server 10.11.11 | Required | Supported server and plugin ABI |
| [Playback Reporting](https://github.com/jellyfin/jellyfin-plugin-playbackreporting) | Required | Historical playback source read only by the scheduled cache job |
| JS Injector | Optional | Adds an Analytics link to the regular user menu |
| [JellySpotlight](https://github.com/skijk/jellyfin-plugin-jellyspotlight) | Optional consumer | Can display Jelana's cached Trending and Popular new arrivals data |

Jelana does not require File Transformation, JellySpotlight, JellyBulletin,
Radarr Watch or the old standalone PHP application.

## Installation

1. Install Playback Reporting from the official Jellyfin plugin catalog and
   allow it to begin recording playback.
2. Add the Jelana repository:

   ```text
   https://raw.githubusercontent.com/skijk/jellyfin-plugin-jelana-repository/main/manifest.json
   ```

3. Install Jelana and restart Jellyfin.
4. Run **Dashboard → Scheduled Tasks → Jelana → Refresh Jelana analytics
   snapshot**, or wait for the startup/hourly refresh.

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
        script.src = ApiClient.getUrl('Jelana/Menu.js', { version: '0.1.22.0' });
        document.head.appendChild(script);
    };

    loadJelanaMenu();
})();
```

The link opens the authenticated user page at `/Jelana/User`. Menu behavior and
future compatibility fixes remain bundled in Jelana itself. Enable
**Requires Authentication** for this script in JS Injector.
