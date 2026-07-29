(() => {
    'use strict';
    const page = document.querySelector('#jelanaPage');
    const pick = (value, name) => value?.[name] ?? value?.[name[0].toUpperCase() + name.slice(1)];
    const duration = seconds => {
        const hours = Math.floor(Number(seconds || 0) / 3600);
        const days = Math.floor(hours / 24);
        return days ? `${days} d ${hours % 24} h` : `${hours} h`;
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
    async function load() {
        const loading = page.querySelector('#jelanaLoading');
        try {
            const data = await ApiClient.ajax({
                type: 'GET',
                url: ApiClient.getUrl('Jelana/Snapshot'),
                dataType: 'json'
            });
            const metrics = [
                ['Visningar · 30 dagar', pick(data, 'plays30Days')],
                ['Tittartid · 30 dagar', duration(pick(data, 'duration30DaysSeconds'))],
                ['Visningar · totalt', pick(data, 'totalPlays')],
                ['Tittartid · totalt', duration(pick(data, 'totalDurationSeconds'))]
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
            list('#jelanaMovies', pick(data, 'topMovies'), row => `${pick(row, 'plays')} visningar`);
            list('#jelanaSeries', pick(data, 'topSeries'), row => `${pick(row, 'plays')} visningar`);
            list('#jelanaUsers', pick(data, 'topUsers'), row => duration(pick(row, 'durationSeconds')));
            list('#jelanaClients', pick(data, 'clients'), row => String(pick(row, 'count')));
            page.querySelector('#jelanaUpdated').textContent =
                `Uppdaterad ${new Date(pick(data, 'generatedAt')).toLocaleString()}`;
            loading.hidden = true;
            page.querySelector('#jelanaContent').hidden = false;
        } catch (error) {
            loading.textContent = 'Ingen snapshot finns ännu. Den skapas automatiskt i bakgrunden.';
        }
    }
    page.addEventListener('pageshow', load);
    if (!window.jQuery) load();
})();
