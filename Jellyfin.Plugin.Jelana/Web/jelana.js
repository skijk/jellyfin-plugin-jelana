(() => {
    'use strict';
    const STYLE_ID = 'jelana-dashboard-styles';
    const VERSION = '0.1.5.0';
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
        stylesheet.href = ApiClient.getUrl('Jelana/Client.css', { version: VERSION });
        document.head.append(stylesheet);
    }
    ensureStyles();
    const page = document.querySelector('#jelanaPage');
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
            const image = document.createElement('img');
            image.className = 'jelana-ranking-thumb';
            image.loading = 'lazy';
            image.alt = '';
            image.src = ApiClient.getUrl(`Items/${pick(row, 'id')}/Images/Primary`, {
                maxWidth: 96,
                quality: 82
            });
            image.addEventListener('error', () => image.classList.add('is-missing'));
            const name = document.createElement('span');
            name.textContent = `${index + 1}. ${pick(row, 'name')}`;
            const count = document.createElement('strong');
            count.textContent = value(row);
            item.append(image, name, count);
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
    async function load() {
        const loading = page.querySelector('#jelanaLoading');
        try {
            const data = await ApiClient.ajax({
                type: 'GET',
                url: ApiClient.getUrl('Jelana/Snapshot'),
                dataType: 'json'
            });
            const metrics = [
                ['Visningar · 30 dagar', pick(pick(data, 'playback30'), 'plays')],
                ['Tittartid · 30 dagar', duration(pick(pick(data, 'playback30'), 'durationSeconds'))],
                ['Visningar · totalt', pick(pick(data, 'playbackAll'), 'plays')],
                ['Tittartid · totalt', duration(pick(pick(data, 'playbackAll'), 'durationSeconds'))]
            ];
            page.querySelector('#jelanaMetrics').replaceChildren(...metrics.map(([label, value]) => {
                const card = document.createElement('article');
                card.className = 'jelana-panel jelana-metric';
                const strong = document.createElement('strong');
                const span = document.createElement('span');
                strong.textContent = String(value);
                span.textContent = label;
                card.append(strong, span);
                return card;
            }));
            rankingList('#jelanaMovies', pick(data, 'topMovies30'), row => `${pick(row, 'plays')} visningar`);
            rankingList('#jelanaSeries', pick(data, 'topSeries30'), row => `${pick(row, 'plays')} visningar`);
            list('#jelanaUsers', pick(data, 'topUsers30'), row => duration(pick(row, 'durationSeconds')));
            list('#jelanaClients', pick(data, 'clients'), row => String(pick(row, 'count')));
            list('#jelanaMethods', pick(data, 'playbackMethods'), row => String(pick(row, 'count')));
            const counts = pick(data, 'counts');
            facts('#jelanaLibrary', [
                ['Filmer', pick(counts, 'movies')],
                ['Serier', pick(counts, 'series')],
                ['Avsnitt', pick(counts, 'episodes')],
                ['Användare', pick(counts, 'users')],
                ['Lagring', bytes(pick(pick(data, 'storage'), 'total'))]
            ]);
            const added = pick(data, 'newItems');
            facts('#jelanaNew7', [
                ['Filmer', pick(added, 'movies7')],
                ['Serier', pick(added, 'series7')]
            ]);
            facts('#jelanaNew30', [
                ['Filmer', pick(added, 'movies30')],
                ['Serier', pick(added, 'series30')]
            ]);
            const profile = pick(data, 'mediaProfile');
            list('#jelanaVideo', dictionaryRows(pick(profile, 'video')), row => String(row.count));
            list('#jelanaResolution', dictionaryRows(pick(profile, 'resolution')), row => String(row.count));
            list('#jelanaAudio', dictionaryRows(pick(profile, 'audio')), row => String(row.count));
            const activity = pick(data, 'activity') || [];
            const maxDuration = Math.max(1, ...activity.map(row => Number(pick(row, 'durationSeconds') || 0)));
            page.querySelector('#jelanaActivity').replaceChildren(...activity.map(row => {
                const wrapper = document.createElement('span');
                wrapper.className = 'jelana-chart-column';
                wrapper.tabIndex = 0;
                const bar = document.createElement('span');
                bar.className = 'jelana-chart-bar';
                bar.style.height = `${Math.max(2, Number(pick(row, 'durationSeconds') || 0) / maxDuration * 100)}%`;
                const tooltip = document.createElement('span');
                tooltip.className = 'jelana-chart-tooltip';
                tooltip.textContent = `${pick(row, 'date')} · ${pick(row, 'plays')} visningar · ${duration(pick(row, 'durationSeconds'))}`;
                wrapper.setAttribute('aria-label', tooltip.textContent);
                wrapper.append(bar, tooltip);
                return wrapper;
            }));
            page.querySelector('#jelanaRecent').replaceChildren(...(pick(data, 'recent') || []).map(item => {
                const link = document.createElement('a');
                link.href = `#!/details?id=${encodeURIComponent(pick(item, 'id'))}`;
                const image = document.createElement('img');
                image.loading = 'lazy';
                image.src = ApiClient.getUrl(`Items/${pick(item, 'id')}/Images/Primary`, { maxWidth: 320, quality: 88 });
                const name = document.createElement('strong');
                name.textContent = pick(item, 'name');
                link.append(image, name);
                return link;
            }));
            page.querySelector('#jelanaUpdated').textContent =
                `Uppdaterad ${new Date(pick(data, 'generatedAt')).toLocaleString('sv-SE', {
                    dateStyle: 'short',
                    timeStyle: 'medium',
                    hour12: false
                })}`;
            loading.hidden = true;
            page.querySelector('#jelanaContent').hidden = false;
        } catch (error) {
            loading.textContent = 'Ingen snapshot finns ännu. Den skapas automatiskt i bakgrunden.';
        }
    }
    page.addEventListener('pageshow', load);
    if (!window.jQuery) load();
})();
