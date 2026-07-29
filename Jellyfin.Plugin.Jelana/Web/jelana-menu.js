(() => {
    'use strict';
    if (window.__jelanaMenuInstalled) return;
    window.__jelanaMenuInstalled = true;

    const addMenuItem = () => {
        const container = document.querySelector('.customMenuOptions');
        if (!container || container.querySelector('[data-jelana-user-menu]')) return;

        const link = document.createElement('a');
        link.className = 'navMenuOption lnkMediaFolder';
        link.dataset.jelanaUserMenu = 'true';
        link.href = typeof ApiClient !== 'undefined'
            ? ApiClient.getUrl('Jelana/User')
            : '/Jelana/User';

        const icon = document.createElement('span');
        icon.className = 'material-icons navMenuOptionIcon analytics';
        icon.setAttribute('aria-hidden', 'true');

        const label = document.createElement('span');
        label.className = 'navMenuOptionText';
        label.textContent = 'Statistik';

        link.append(icon, label);
        container.prepend(link);
    };

    const observer = new MutationObserver(addMenuItem);
    observer.observe(document.documentElement, { childList: true, subtree: true });
    document.addEventListener('viewshow', addMenuItem);
    addMenuItem();
})();
