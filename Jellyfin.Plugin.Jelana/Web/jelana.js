(() => {
    'use strict';
    const STYLE_ID = 'jelana-dashboard-styles';
    const VERSION = '0.1.19.0';
    const embeddedInJellyfin = typeof window.ApiClient !== 'undefined';
    const basePath = embeddedInJellyfin
        ? ''
        : location.pathname.slice(0, location.pathname.toLowerCase().lastIndexOf('/jelana/user'));
    let accessToken = '';
    if (!embeddedInJellyfin) {
        document.documentElement.classList.add('jelana-standalone');
        try {
            const credentials = JSON.parse(localStorage.getItem('jellyfin_credentials') || '{}');
            const servers = credentials.Servers || [];
            const sameServer = servers.find(server =>
                [server.Address, server.ManualAddress, server.LocalAddress]
                    .filter(Boolean)
                    .some(address => {
                        try {
                            const url = new URL(address, location.origin);
                            return url.origin === location.origin && location.pathname.startsWith(url.pathname.replace(/\/$/, ''));
                        } catch {
                            return false;
                        }
                    }));
            accessToken = (sameServer || servers.find(server => server.AccessToken))?.AccessToken || '';
        } catch {
            accessToken = '';
        }
    }
    const getUrl = (path, parameters = {}) => {
        if (embeddedInJellyfin) return ApiClient.getUrl(path, parameters);
        const url = new URL(`${basePath}/${path}`.replace(/\/{2,}/g, '/'), location.origin);
        Object.entries(parameters).forEach(([name, value]) => url.searchParams.set(name, value));
        if (accessToken && path.includes('/Images/')) url.searchParams.set('api_key', accessToken);
        return url.toString();
    };
    const detailUrl = id => embeddedInJellyfin
        ? `#!/details?id=${encodeURIComponent(id)}`
        : `${basePath}/web/#/details?id=${encodeURIComponent(id)}`;
    const getSnapshot = async () => {
        if (embeddedInJellyfin) {
            return ApiClient.ajax({
                type: 'GET',
                url: getUrl('Jelana/Snapshot'),
                dataType: 'json'
            });
        }
        const response = await fetch(getUrl('Jelana/Snapshot'), {
            headers: accessToken ? { 'X-Emby-Token': accessToken } : {}
        });
        if (response.status === 401 || response.status === 403) {
            throw new Error('AUTH_REQUIRED');
        }
        if (!response.ok) throw new Error(`HTTP_${response.status}`);
        return response.json();
    };
    const getPersonal = async () => {
        if (embeddedInJellyfin) {
            return ApiClient.ajax({
                type: 'GET',
                url: getUrl('Jelana/Personal'),
                dataType: 'json'
            });
        }
        const response = await fetch(getUrl('Jelana/Personal'), {
            headers: accessToken ? { 'X-Emby-Token': accessToken } : {}
        });
        if (!response.ok) throw new Error(`HTTP_${response.status}`);
        return response.json();
    };
    function ensureStyles() {
        let stylesheet = document.getElementById(STYLE_ID);
        if (stylesheet) {
            if (stylesheet.dataset.version === VERSION) return;
            stylesheet.remove();
        }

        stylesheet = document.createElement('link');
        stylesheet.id = STYLE_ID;
        stylesheet.dataset.version = VERSION;
        stylesheet.rel = 'stylesheet';
        stylesheet.href = getUrl('Jelana/Client.css', { version: VERSION });
        document.head.append(stylesheet);
    }
    ensureStyles();
    const page = document.querySelector('#jelanaPage');
    const home = page.querySelector('#jelanaHome');
    home.href = embeddedInJellyfin ? '#/home' : `${basePath}/web/#/home`;
    const pluginLogoUrl = getUrl('Jelana/Logo.png', { version: VERSION });
    const homeLogo = page.querySelector('#jelanaHomeLogo');
    homeLogo.addEventListener('error', () => {
        if (homeLogo.src !== pluginLogoUrl) homeLogo.src = pluginLogoUrl;
    }, { once: true });
    homeLogo.src = getUrl('Branding/Splashscreen', { version: VERSION });
    page.querySelector('#jelanaBrandLogo').src = pluginLogoUrl;
    let loadedPersonal = null;
    function setupTabs() {
        page.querySelectorAll('[data-jelana-tabs]').forEach(panel => {
            const buttons = panel.querySelectorAll('[data-tab]');
            const contents = panel.querySelectorAll('[data-tab-content]');
            buttons.forEach(button => {
                button.setAttribute('role', 'tab');
                button.setAttribute('aria-selected', String(button.classList.contains('is-active')));
                button.addEventListener('click', () => {
                    const selected = button.dataset.tab;
                    buttons.forEach(candidate => {
                        const active = candidate === button;
                        candidate.classList.toggle('is-active', active);
                        candidate.setAttribute('aria-selected', String(active));
                    });
                    contents.forEach(content => {
                        content.hidden = content.dataset.tabContent !== selected;
                    });
                    if (panel.dataset.jelanaTabs === 'personal') {
                        renderPersonalHabits(selected);
                    }
                });
            });
        });
    }
    setupTabs();
    const pick = (value, name) => value?.[name] ?? value?.[name[0].toUpperCase() + name.slice(1)];
    const duration = seconds => {
        const hours = Math.floor(Number(seconds || 0) / 3600);
        const days = Math.floor(hours / 24);
        return days ? `${days} d ${hours % 24} h` : `${hours} h`;
    };
    const bytes = value => {
        if (value === null || value === undefined) return '–';
        const units = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'];
        let number = Number(value);
        let unit = 0;
        while (number >= 1024 && unit < units.length - 1) { number /= 1024; unit += 1; }
        return `${number.toFixed(unit >= 3 ? 1 : 0)} ${units[unit]}`;
    };
    const list = (id, rows, value) => {
        const target = page.querySelector(id);
        target.replaceChildren(...(rows || []).map((row, index) => {
            const item = document.createElement('li');
            const name = document.createElement('span');
            const count = document.createElement('strong');
            name.textContent = `${index + 1}. ${pick(row, 'name')}`;
            count.textContent = value(row);
            item.append(name, count);
            return item;
        }));
    };
    const rankingList = (id, rows, value) => {
        const target = page.querySelector(id);
        target.replaceChildren(...(rows || []).map((row, index) => {
            const item = document.createElement('li');
            item.className = 'jelana-ranking-item';
            const link = document.createElement('a');
            link.className = 'jelana-ranking-link';
            link.href = detailUrl(pick(row, 'id'));
            const image = document.createElement('img');
            image.className = 'jelana-ranking-thumb';
            image.loading = 'lazy';
            image.alt = '';
            image.src = getUrl(`Items/${pick(row, 'id')}/Images/Primary`, {
                maxWidth: 96,
                quality: 82
            });
            image.addEventListener('error', () => image.classList.add('is-missing'));
            const name = document.createElement('span');
            name.textContent = `${index + 1}. ${pick(row, 'name')}`;
            link.append(image, name);
            const count = document.createElement('strong');
            count.textContent = value(row);
            item.append(link, count);
            return item;
        }));
    };
    const facts = (id, rows) => {
        const target = page.querySelector(id);
        target.replaceChildren(...rows.map(([label, value]) => {
            const row = document.createElement('div');
            const span = document.createElement('span');
            const strong = document.createElement('strong');
            span.textContent = label;
            strong.textContent = String(value);
            row.append(span, strong);
            return row;
        }));
    };
    const dictionaryRows = value =>
        Object.entries(value || {}).map(([name, count]) => ({ name, count }));
    const percentChange = (current, previous) => {
        current = Number(current || 0);
        previous = Number(previous || 0);
        if (previous === 0) return current === 0 ? 0 : 100;
        return Math.round((current - previous) * 100 / previous);
    };
    const signedPercent = value => `${value > 0 ? '+' : ''}${value}%`;
    const personalFacts = (id, period) => facts(id, [
        ['Movie plays', pick(period, 'movies')],
        ['Episode plays', pick(period, 'episodes')],
        ['Watch time', duration(pick(period, 'durationSeconds'))]
    ]);
    const renderPersonalHabits = selected => {
        if (!loadedPersonal) return;
        const periods = {
            personal30: ['habits30Days', 'Split over the last 30 days'],
            personal365: ['habitsLastYear', 'Split over the last year'],
            personalAll: ['habitsAllTime', 'All-time split']
        };
        const [property, caption] = periods[selected] || periods.personal30;
        const habits = pick(loadedPersonal, property);
        page.querySelector('#jelanaFavoriteDay').textContent = pick(habits, 'favoriteWeekday');
        page.querySelector('#jelanaFavoriteTime').textContent = pick(habits, 'favoriteTimeOfDay');
        page.querySelector('#jelanaLongestSession').textContent =
            duration(pick(habits, 'longestSessionSeconds'));
        const moviePercent = Number(pick(habits, 'moviePercent') || 0);
        const episodePercent = Number(pick(habits, 'episodePercent') || 0);
        page.querySelector('#jelanaMoviePercent').textContent = `${moviePercent}%`;
        page.querySelector('#jelanaEpisodePercent').textContent = `${episodePercent}%`;
        page.querySelector('#jelanaSplitPeriod').textContent = caption;
        page.querySelector('#jelanaMediaDonut').style.setProperty('--movie-share', `${moviePercent}%`);
    };
    async function load() {
        const loading = page.querySelector('#jelanaLoading');
        try {
            const data = await getSnapshot();
            page.querySelector('#jelanaPlays30').textContent =
                String(pick(pick(data, 'playback30'), 'plays'));
            page.querySelector('#jelanaPlaysAll').textContent =
                String(pick(pick(data, 'playbackAll'), 'plays'));
            page.querySelector('#jelanaWatch30').textContent =
                duration(pick(pick(data, 'playback30'), 'durationSeconds'));
            page.querySelector('#jelanaWatchAll').textContent =
                duration(pick(pick(data, 'playbackAll'), 'durationSeconds'));
            [
                ['#jelanaMovies7', 'topMovies7'],
                ['#jelanaMovies30', 'topMovies30'],
                ['#jelanaMoviesAll', 'topMoviesAll']
            ].forEach(([selector, property]) => rankingList(selector, pick(data, property), row =>
                `${pick(row, 'plays')} plays · ${pick(row, 'uniqueViewers')} viewers`));
            [
                ['#jelanaSeries7', 'topSeries7'],
                ['#jelanaSeries30', 'topSeries30'],
                ['#jelanaSeriesAll', 'topSeriesAll']
            ].forEach(([selector, property]) => rankingList(selector, pick(data, property), row =>
                `${pick(row, 'plays')} plays · ${pick(row, 'uniqueViewers')} viewers`));
            [
                ['#jelanaUsers7', 'topUsers7'],
                ['#jelanaUsers30', 'topUsers30'],
                ['#jelanaUsersAll', 'topUsersAll']
            ].forEach(([selector, property]) =>
                list(selector, pick(data, property), row => duration(pick(row, 'durationSeconds'))));
            list('#jelanaClients', pick(data, 'clients'), row => String(pick(row, 'count')));
            list('#jelanaMethods', pick(data, 'playbackMethods'), row => String(pick(row, 'count')));
            const counts = pick(data, 'counts');
            facts('#jelanaLibrary', [
                ['Movies', pick(counts, 'movies')],
                ['Series', pick(counts, 'series')],
                ['Episodes', pick(counts, 'episodes')],
                ['Users', pick(counts, 'users')],
                ['Storage', bytes(pick(pick(data, 'storage'), 'total'))]
            ]);
            const added = pick(data, 'newItems');
            facts('#jelanaNew7', [
                ['Movies', pick(added, 'movies7')],
                ['Series', pick(added, 'series7')]
            ]);
            facts('#jelanaNew30', [
                ['Movies', pick(added, 'movies30')],
                ['Series', pick(added, 'series30')]
            ]);
            const profile = pick(data, 'mediaProfile');
            list('#jelanaVideo', dictionaryRows(pick(profile, 'video')), row => String(row.count));
            list('#jelanaResolution', dictionaryRows(pick(profile, 'resolution')), row => String(row.count));
            list('#jelanaAudio', dictionaryRows(pick(profile, 'audio')), row => String(row.count));
            const monthly = pick(data, 'monthlyTrend');
            const monthlyCurrent = pick(monthly, 'current');
            const monthlyPrevious = pick(monthly, 'previous');
            const playsChange = percentChange(pick(monthlyCurrent, 'plays'), pick(monthlyPrevious, 'plays'));
            const durationChange = percentChange(
                pick(monthlyCurrent, 'durationSeconds'),
                pick(monthlyPrevious, 'durationSeconds'));
            const monthlyTarget = page.querySelector('#jelanaMonthlyTrend');
            monthlyTarget.replaceChildren(...[
                ['Plays', pick(monthlyCurrent, 'plays'), playsChange],
                ['Watch time', duration(pick(monthlyCurrent, 'durationSeconds')), durationChange]
            ].map(([label, value, change]) => {
                const box = document.createElement('div');
                const span = document.createElement('span');
                const strong = document.createElement('strong');
                const delta = document.createElement('small');
                span.textContent = label;
                strong.textContent = String(value);
                delta.textContent = signedPercent(change);
                delta.className = change >= 0 ? 'jelana-trend-positive' : 'jelana-trend-negative';
                box.append(span, strong, delta);
                return box;
            }));
            page.querySelector('#jelanaTrending').replaceChildren(...(pick(data, 'trending') || []).map((row, index) => {
                const item = document.createElement('li');
                const link = document.createElement('a');
                link.className = 'jelana-ranking-link';
                link.href = detailUrl(pick(row, 'id'));
                link.textContent = `${index + 1}. ${pick(row, 'name')}`;
                const meta = document.createElement('span');
                meta.className = 'jelana-trending-meta';
                const type = document.createElement('small');
                type.className = 'jelana-trending-type';
                type.textContent = pick(row, 'type');
                const current = Number(pick(row, 'currentPlays') || 0);
                const previous = Number(pick(row, 'previousPlays') || 0);
                const difference = current - previous;
                const comparison = difference > 0
                    ? `↑ ${difference} vs last week`
                    : difference < 0
                        ? `↓ ${Math.abs(difference)} vs last week`
                        : 'no change vs last week';
                const viewers = Number(pick(row, 'uniqueViewers') || 0);
                const value = document.createElement('strong');
                value.textContent =
                    `${current} ${current === 1 ? 'play' : 'plays'} · ${comparison} · ${viewers} ${viewers === 1 ? 'viewer' : 'viewers'}`;
                meta.append(type, value);
                item.append(link, meta);
                return item;
            }));
            const activity = pick(data, 'activity') || [];
            const maxDuration = Math.max(1, ...activity.map(row => Number(pick(row, 'durationSeconds') || 0)));
            page.querySelector('#jelanaActivity').replaceChildren(...activity.map((row, index) => {
                const wrapper = document.createElement('span');
                wrapper.className = 'jelana-chart-column';
                wrapper.tabIndex = 0;
                const bar = document.createElement('span');
                bar.className = 'jelana-chart-bar';
                bar.style.setProperty(
                    '--jelana-bar-height',
                    `${Math.max(2, Number(pick(row, 'durationSeconds') || 0) / maxDuration * 100)}%`
                );
                const tooltip = document.createElement('span');
                tooltip.className = 'jelana-chart-tooltip';
                tooltip.textContent = `${pick(row, 'date')} · ${pick(row, 'plays')} plays · ${duration(pick(row, 'durationSeconds'))}`;
                wrapper.setAttribute('aria-label', tooltip.textContent);
                const dateLabel = document.createElement('span');
                dateLabel.className = 'jelana-chart-date';
                const showDate = index === 0 || index === activity.length - 1 || index % 5 === 0;
                if (showDate) {
                    dateLabel.textContent = new Date(`${pick(row, 'date')}T12:00:00`).toLocaleDateString('en-GB', {
                        month: 'short',
                        day: 'numeric'
                    });
                }
                wrapper.append(bar, tooltip, dateLabel);
                return wrapper;
            }));
            page.querySelector('#jelanaUpdated').textContent =
                `Updated ${new Date(pick(data, 'generatedAt')).toLocaleString('en-GB', {
                    dateStyle: 'short',
                    timeStyle: 'medium',
                    hour12: false
                })}`;
            loading.hidden = true;
            page.querySelector('#jelanaContent').hidden = false;
            try {
                const personal = await getPersonal();
                personalFacts('#jelanaPersonal30', pick(personal, 'last30Days'));
                personalFacts('#jelanaPersonal365', pick(personal, 'lastYear'));
                personalFacts('#jelanaPersonalAll', pick(personal, 'allTime'));
                loadedPersonal = personal;
                const selected = page.querySelector('[data-jelana-tabs="personal"] [data-tab].is-active')?.dataset.tab;
                renderPersonalHabits(selected || 'personal30');
                page.querySelector('#jelanaPersonalPanel').hidden = false;
            } catch {
                page.querySelector('#jelanaPersonalPanel').hidden = true;
            }
        } catch (error) {
            if (error?.message === 'AUTH_REQUIRED') {
                loading.replaceChildren();
                const message = document.createElement('span');
                const login = document.createElement('a');
                message.textContent = 'You need to be signed in to Jellyfin to view analytics. ';
                login.href = `${basePath}/web/`;
                login.textContent = 'Sign in';
                loading.append(message, login);
            } else {
                loading.textContent = 'No snapshot is available yet. It is created automatically in the background.';
            }
        }
    }
    page.addEventListener('pageshow', load);
    if (!window.jQuery || !embeddedInJellyfin) load();
})();
