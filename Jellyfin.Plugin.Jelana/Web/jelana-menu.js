(() => {
    'use strict';
    if (window.__jelanaMenuInstalled) return;
    window.__jelanaMenuInstalled = true;

    const itemSelector = '[data-jelana-user-menu]';
    const analyticsUrl = () => {
        const url = typeof ApiClient !== 'undefined'
            ? ApiClient.getUrl('Jelana/User')
            : '/Jelana/User';
        if (typeof ApiClient === 'undefined' || typeof ApiClient.accessToken !== 'function') return url;
        const token = ApiClient.accessToken();
        return token ? `${url}#jelana_token=${encodeURIComponent(token)}` : url;
    };
    const integratedAnalyticsUrl = () => '#/userpluginsettings.html?pageUrl=/Jelana/User';

    const rememberAccessToken = () => {
        if (typeof ApiClient === 'undefined' || typeof ApiClient.accessToken !== 'function') return;
        const token = ApiClient.accessToken();
        if (token) sessionStorage.setItem('jelana_access_token', token);
    };

    const setDestination = (item, destination = analyticsUrl) => {
        const link = item.matches('a') ? item : item.querySelector('a');
        if (link) {
            link.href = destination();
            link.addEventListener('click', rememberAccessToken);
            return;
        }

        item.setAttribute('role', 'menuitem');
        item.tabIndex = 0;
        const open = () => {
            rememberAccessToken();
            window.location.href = destination();
        };
        item.addEventListener('click', open);
        item.addEventListener('keydown', (event) => {
            if (event.key === 'Enter' || event.key === ' ') open();
        });
    };

    const replaceLabelAndIcon = (item, oldLabel) => {
        const walker = document.createTreeWalker(item, NodeFilter.SHOW_TEXT);
        const textNodes = [];
        while (walker.nextNode()) textNodes.push(walker.currentNode);

        const nonEmptyTextNodes = textNodes.filter((node) => node.nodeValue.trim());
        const label = textNodes.find((node) => node.nodeValue.trim() === oldLabel)
            || nonEmptyTextNodes[nonEmptyTextNodes.length - 1];
        if (label) label.nodeValue = label.nodeValue.replace(oldLabel, 'Analytics');

        const icon = item.querySelector('.material-icons, .material-icons-round, .material-symbols-rounded');
        if (icon) icon.textContent = 'analytics';
    };

    const addJellyfin12MenuItem = () => {
        const enhanced = document.getElementById('jellyfinEnhancedUserMenuLink');
        const dashboard = Array.from(document.querySelectorAll('[role="menuitem"], a, button'))
            .find((item) => item.textContent.trim() === 'Dashboard');
        const reference = enhanced || dashboard;
        if (!reference) return false;

        const container = reference.closest('[role="menu"], ul, .MuiMenu-list') || reference.parentElement;
        if (!container) return false;
        if (container.querySelector(itemSelector)) return true;

        const item = reference.cloneNode(true);
        item.removeAttribute('id');
        item.dataset.jelanaUserMenu = 'true';
        replaceLabelAndIcon(item, enhanced ? 'Jellyfin Enhanced' : 'Dashboard');
        setDestination(item, integratedAnalyticsUrl);

        if (enhanced) enhanced.after(item);
        else dashboard.before(item);
        return true;
    };

    const addLegacyMenuItem = () => {
        const container = document.querySelector('.customMenuOptions');
        if (!container || container.querySelector(itemSelector)) return false;

        const link = document.createElement('a');
        link.className = 'navMenuOption lnkMediaFolder';
        link.dataset.jelanaUserMenu = 'true';
        link.href = analyticsUrl();

        const icon = document.createElement('span');
        icon.className = 'material-icons navMenuOptionIcon analytics';
        icon.setAttribute('aria-hidden', 'true');

        const label = document.createElement('span');
        label.className = 'navMenuOptionText';
        label.textContent = 'Analytics';

        link.append(icon, label);
        container.prepend(link);
        return true;
    };

    const addMenuItem = () => {
        addJellyfin12MenuItem() || addLegacyMenuItem();
    };

    const observer = new MutationObserver(addMenuItem);
    observer.observe(document.documentElement, { childList: true, subtree: true });
    document.addEventListener('viewshow', addMenuItem);
    addMenuItem();
})();
