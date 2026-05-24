(function () {
    'use strict';

    var mq = window.matchMedia('(max-width: 768px)');

    function getEls() {
        return {
            toggle: document.getElementById('nav-toggle'),
            backdrop: document.getElementById('sidebar-backdrop'),
            sidebar: document.getElementById('app-sidebar')
        };
    }

    function isOpen() {
        return document.body.classList.contains('nav-open');
    }

    function setOpen(open) {
        var els = getEls();
        document.body.classList.toggle('nav-open', open);
        if (els.toggle) {
            els.toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
            els.toggle.setAttribute('aria-label', open ? 'Закрыть меню' : 'Открыть меню');
        }
        if (els.backdrop) {
            els.backdrop.setAttribute('aria-hidden', open ? 'false' : 'true');
        }
        document.documentElement.style.overflow = open ? 'hidden' : '';
    }

    function closeNav() {
        setOpen(false);
    }

    function toggleNav() {
        setOpen(!isOpen());
    }

    function bindNavLinks() {
        var els = getEls();
        if (!els.sidebar) return;
        els.sidebar.querySelectorAll('a[href]').forEach(function (link) {
            if (link.dataset.navBound === '1') return;
            link.dataset.navBound = '1';
            link.addEventListener('click', function () {
                if (mq.matches) closeNav();
            });
        });
    }

    function init() {
        var els = getEls();
        if (!els.toggle || !els.sidebar) return;

        if (els.toggle.dataset.navInit === '1') {
            bindNavLinks();
            return;
        }
        els.toggle.dataset.navInit = '1';

        els.toggle.addEventListener('click', toggleNav);
        if (els.backdrop) {
            els.backdrop.addEventListener('click', closeNav);
        }

        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && isOpen()) closeNav();
        });

        mq.addEventListener('change', function () {
            if (!mq.matches) closeNav();
        });

        bindNavLinks();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    if (typeof Blazor !== 'undefined') {
        Blazor.addEventListener('enhancedload', function () {
            requestAnimationFrame(function () {
                bindNavLinks();
                if (!mq.matches) closeNav();
            });
        });
    }

    window.mobileNav = { close: closeNav, toggle: toggleNav, refresh: init };
})();
