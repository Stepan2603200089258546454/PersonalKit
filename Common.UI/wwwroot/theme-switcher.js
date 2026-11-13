/* ===================================================
   Common.UI / Переключатель темы
   Выбранная тема хранится в localStorage, итоговая (light/dark) —
   ещё и в cookie: серверный App.razor читает cookie и отдаёт
   в разметке то же значение, поэтому тема не слетает при переходе
   на интерактивные (WASM) страницы и при навигации.
   =================================================== */

(function () {
    if (window.__cuiThemeLoaded) {
        return;
    }

    window.__cuiThemeLoaded = true;

    var storageKey = 'cui.theme';
    var labels = { light: 'Светлая', dark: 'Тёмная', auto: 'Системная' };
    var icons = { light: 'bi-sun-fill', dark: 'bi-moon-stars-fill', auto: 'bi-circle-half' };

    function getSelected() {
        try {
            var value = localStorage.getItem(storageKey);
            return value === 'light' || value === 'dark' ? value : 'auto';
        } catch (e) {
            return 'auto';
        }
    }

    function saveSelected(theme) {
        try {
            localStorage.setItem(storageKey, theme);
        } catch (e) {
            // Приватный режим браузера может запрещать localStorage.
        }
    }

    function systemTheme() {
        return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    }

    function resolve(theme) {
        return theme === 'auto' ? systemTheme() : theme;
    }

    function writeCookie(resolved) {
        try {
            // Значение cookie — итоговая тема для серверной отрисовки.
            document.cookie = storageKey + '=' + resolved + '; path=/; max-age=31536000; SameSite=Lax';
        } catch (e) {
            // Cookie может быть запрещена настройками браузера.
        }
    }

    function setDocumentTheme(theme) {
        var expected = resolve(theme);

        if (document.documentElement.getAttribute('data-bs-theme') !== expected) {
            document.documentElement.setAttribute('data-bs-theme', expected);
        }

        return expected;
    }

    function updateSwitchers(selected) {
        document.querySelectorAll('[data-cui-theme]').forEach(function (root) {
            root.querySelectorAll('[data-cui-theme-value]').forEach(function (item) {
                var value = item.getAttribute('data-cui-theme-value');
                var active = value === selected;

                item.classList.toggle('active', active);
                item.setAttribute('aria-checked', active ? 'true' : 'false');
            });

            var label = root.querySelector('[data-cui-theme-label]');

            if (label) {
                label.textContent = labels[selected] || 'Тема';
            }

            var icon = root.querySelector('[data-cui-theme-icon]');

            if (icon) {
                icon.className = 'bi ' + (icons[selected] || 'bi-circle-half');
            }
        });
    }

    function apply(theme, persist) {
        if (persist) {
            saveSelected(theme);
        }

        var resolved = setDocumentTheme(theme);

        updateSwitchers(theme);
        writeCookie(resolved);

        document.dispatchEvent(new CustomEvent('cui-theme-changed', {
            detail: { selected: theme, resolved: resolved }
        }));
    }

    function refresh() {
        var selected = getSelected();
        var resolved = setDocumentTheme(selected);

        updateSwitchers(selected);
        writeCookie(resolved);
    }

    document.addEventListener('click', function (event) {
        var target = event.target instanceof Element ? event.target : null;

        if (!target) {
            return;
        }

        var item = target.closest('[data-cui-theme-value]');

        if (!item) {
            return;
        }

        var theme = item.getAttribute('data-cui-theme-value');

        if (theme === 'light' || theme === 'dark' || theme === 'auto') {
            apply(theme, true);
        }
    });

    // Смена системной темы применяется, только если выбрано «Системная».
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function () {
        if (getSelected() === 'auto') {
            refresh();
        }
    });

    // Тема, выбранная в другой вкладке, подхватывается и здесь.
    window.addEventListener('storage', function (event) {
        if (event.key === storageKey) {
            refresh();
        }
    });

    // Усиленная навигация Blazor заменяет атрибуты html на серверные,
    // поэтому тему нужно применить заново (плюс на всякий случай — на popstate).
    ['enhancedload', 'enhanced-navigation:load', 'blazor:navigated'].forEach(function (eventName) {
        document.addEventListener(eventName, refresh);
    });

    window.addEventListener('popstate', refresh);
    window.addEventListener('pageshow', refresh);

    // Если разметку html изменил кто-то ещё (фреймворк, другой скрипт),
    // возвращаем фактически выбранную тему.
    if (typeof MutationObserver !== 'undefined') {
        var observer = new MutationObserver(function () {
            var expected = resolve(getSelected());

            if (document.documentElement.getAttribute('data-bs-theme') !== expected) {
                document.documentElement.setAttribute('data-bs-theme', expected);
            }
        });

        observer.observe(document.documentElement, {
            attributes: true,
            attributeFilter: ['data-bs-theme']
        });
    }

    // Публичный API для приложений без Blazor-разметки (гибридные приложения).
    window.cuiTheme = {
        get: getSelected,
        set: function (theme) {
            apply(theme, true);
        }
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', refresh, { once: true });
    } else {
        refresh();
    }
})();
