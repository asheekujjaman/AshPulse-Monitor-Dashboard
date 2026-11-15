// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

(() => {
    const body = document.body;
    const copyableIpSelector = '[data-copy-ip]';

    const addIpCopyButton = container => {
        if (container.querySelector('[data-copy-ip-button]')) return;
        const value = container.getAttribute('data-copy-ip');
        if (!value) return;

        container.classList.add('copyable-ip');
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'copy-ip-button';
        button.setAttribute('data-copy-ip-button', '');
        button.setAttribute('aria-label', 'Copy IP address');
        button.title = 'Copy IP address';
        button.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="8" y="8" width="12" height="12" rx="2"></rect><path d="M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2"></path></svg>';
        container.append(button);
    };

    const enhanceIpCopyButtons = root => {
        if (root.matches?.(copyableIpSelector)) addIpCopyButton(root);
        root.querySelectorAll?.(copyableIpSelector).forEach(addIpCopyButton);
    };

    const copyText = async value => {
        if (navigator.clipboard?.writeText) {
            await navigator.clipboard.writeText(value);
            return;
        }

        const fallback = document.createElement('textarea');
        fallback.value = value;
        fallback.setAttribute('readonly', '');
        fallback.style.position = 'fixed';
        fallback.style.opacity = '0';
        document.body.append(fallback);
        fallback.select();
        const copied = document.execCommand('copy');
        fallback.remove();
        if (!copied) throw new Error('Clipboard is unavailable');
    };

    enhanceIpCopyButtons(document);
    new MutationObserver(records => records.forEach(record => record.addedNodes.forEach(node => {
        if (node.nodeType === Node.ELEMENT_NODE) enhanceIpCopyButtons(node);
    }))).observe(document.body, { childList: true, subtree: true });

    document.addEventListener('click', async event => {
        const button = event.target.closest('[data-copy-ip-button]');
        if (!button) return;

        event.preventDefault();
        event.stopPropagation();
        const container = button.closest(copyableIpSelector);
        const value = container?.getAttribute('data-copy-ip');
        if (!value) return;

        try {
            await copyText(value);
            button.classList.add('is-copied');
            button.setAttribute('aria-label', 'IP address copied');
            button.title = 'Copied';
            button.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m5 12 4 4L19 6"></path></svg>';
        } catch {
            button.setAttribute('aria-label', 'Could not copy IP address');
            button.title = 'Copy failed';
        }

        window.setTimeout(() => {
            if (!button.isConnected) return;
            button.classList.remove('is-copied');
            button.setAttribute('aria-label', 'Copy IP address');
            button.title = 'Copy IP address';
            button.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="8" y="8" width="12" height="12" rx="2"></rect><path d="M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2"></path></svg>';
        }, 1400);
    });

    const sidebar = document.getElementById('app-sidebar');
    if (!sidebar) return;

    const openButton = document.querySelector('[data-sidebar-toggle]');
    const closeButton = document.querySelector('[data-sidebar-close]');
    const isMobile = () => window.matchMedia('(max-width: 760px)').matches;

    const setOpen = open => {
        body.classList.toggle('sidebar-open', open);
        if (openButton) {
            openButton.setAttribute('aria-expanded', String(open));
            openButton.setAttribute('aria-label', open ? 'Close navigation' : 'Open navigation');
        }
    };

    openButton?.addEventListener('click', () => setOpen(!body.classList.contains('sidebar-open')));
    closeButton?.addEventListener('click', () => setOpen(false));
    sidebar.querySelectorAll('a').forEach(link => link.addEventListener('click', () => {
        if (isMobile()) setOpen(false);
    }));
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && body.classList.contains('sidebar-open')) setOpen(false);
    });
    window.matchMedia('(min-width: 761px)').addEventListener('change', () => setOpen(false));
})();
