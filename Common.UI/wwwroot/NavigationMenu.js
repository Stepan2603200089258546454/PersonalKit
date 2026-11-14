/* ===================================================
   Common.UI / Скрипт меню навигации
   Свёртывание боковой панели на десктопе, сворачивание
   разделов меню и закрытие мобильного offcanvas
   после выбора пункта.
   =================================================== */

(function () {
    if (window.__cuiNavigationMenuLoaded) {
        return;
    }

    window.__cuiNavigationMenuLoaded = true;

    var storageKey = 'cui.sidebarCollapsed';
    // Версия в ключе: старые значения, сохранённые с ошибкой в определении
    // состояния раздела, не должны схлопывать меню после обновления.
    var sectionKeyPrefix = 'cui.section.v2.';
    var collapsedClass = 'cui-sidebar-collapsed';
    var desktopQuery = window.matchMedia('(min-width: 992px)');
    var userToggled = false;

    function isDesktop() {
        return desktopQuery.matches;
    }

    function isCollapsed() {
        return document.body.classList.contains(collapsedClass);
    }

    function readStored() {
        try {
            return localStorage.getItem(storageKey) === '1';
        } catch (e) {
            return false;
        }
    }

    function setCollapsed(value, persist) {
        document.body.classList.toggle(collapsedClass, value);

        if (persist) {
            try {
                localStorage.setItem(storageKey, value ? '1' : '0');
            } catch (e) {
                // Приватный режим браузера может запрещать localStorage.
            }
        }

        // В свёрнутой панели текст скрыт — включаем подсказки с названиями страниц.
        syncNavTooltips();
    }

    function applyInitialState() {
        if (!isDesktop()) {
            // На мобильном меню открывается offcanvas, свёрнутое состояние не нужно.
            document.body.classList.remove(collapsedClass);
            return;
        }

        if (!userToggled) {
            setCollapsed(readStored(), false);
        }
    }

    // После усиленной навигации Blazor возвращает серверный класс body,
    // поэтому свёрнутое состояние нужно восстановить из localStorage.
    function restoreCollapsedState() {
        if (!isDesktop()) {
            document.body.classList.remove(collapsedClass);
            return;
        }

        setCollapsed(readStored(), false);
    }

    function onBreakpointChange() {
        if (!isDesktop()) {
            document.body.classList.remove(collapsedClass);
        } else if (!userToggled) {
            setCollapsed(readStored(), false);
        }

        syncNavTooltips();
    }

    /* ===============================
       Разделы меню
       =============================== */

    function readSectionState(key) {
        try {
            var value = localStorage.getItem(sectionKeyPrefix + key);
            return value === '1' ? true : value === '0' ? false : null;
        } catch (e) {
            return null;
        }
    }

    function saveSectionState(key, expanded) {
        try {
            localStorage.setItem(sectionKeyPrefix + key, expanded ? '1' : '0');
        } catch (e) {
            // Приватный режим браузера может запрещать localStorage.
        }
    }

    function findTrigger(element) {
        if (!element.id) {
            return null;
        }

        return document.querySelector('[data-bs-target="#' + element.id + '"]');
    }

    function setTriggerState(element, expanded) {
        var trigger = findTrigger(element);

        if (trigger) {
            trigger.setAttribute('aria-expanded', expanded ? 'true' : 'false');
        }
    }

    function restoreSections() {
        document.querySelectorAll('[data-cui-section-key]').forEach(function (element) {
            var state = readSectionState(element.getAttribute('data-cui-section-key'));

            if (state === false) {
                // Сразу убираем класс: без анимации при загрузке страницы.
                element.classList.remove('show');
                setTriggerState(element, false);
            }
        });
    }

    function syncSections(key, expanded, source) {
        document.querySelectorAll('[data-cui-section-key]').forEach(function (element) {
            if (element === source || element.getAttribute('data-cui-section-key') !== key) {
                return;
            }

            if (!window.bootstrap || !window.bootstrap.Collapse) {
                return;
            }

            var collapse = window.bootstrap.Collapse.getOrCreateInstance(element, { toggle: false });

            if (expanded) {
                collapse.show();
            } else {
                collapse.hide();
            }
        });
    }

    function isSection(element) {
        return element instanceof Element && element.hasAttribute('data-cui-section-key');
    }

    // show.bs.collapse и hide.bs.collapse приходят до анимации:
    // по ним сразу переключаем стрелку, чтобы она поворачивалась вместе с кликом.
    function onSectionChanging(event, expanded) {
        if (!isSection(event.target)) {
            return;
        }

        setTriggerState(event.target, expanded);
    }

    // shown.bs.collapse и hidden.bs.collapse приходят после анимации:
    // по ним сохраняем состояние и повторяем его во второй копии меню.
    function onSectionChanged(event, expanded) {
        var element = event.target;

        if (!isSection(element)) {
            return;
        }

        var key = element.getAttribute('data-cui-section-key');

        saveSectionState(key, expanded);
        setTriggerState(element, expanded);
        syncSections(key, expanded, element);
    }

    document.addEventListener('show.bs.collapse', function (event) {
        onSectionChanging(event, true);
    });

    document.addEventListener('hide.bs.collapse', function (event) {
        onSectionChanging(event, false);
    });

    document.addEventListener('shown.bs.collapse', function (event) {
        onSectionChanged(event, true);
    });

    document.addEventListener('hidden.bs.collapse', function (event) {
        onSectionChanged(event, false);
    });

    /* ===============================
       Подсказки в свёрнутой панели
       =============================== */

    function tooltipApi() {
        return window.bootstrap && window.bootstrap.Tooltip ? window.bootstrap.Tooltip : null;
    }

    function enableNavTooltips() {
        var api = tooltipApi();

        if (!api) {
            return;
        }

        document.querySelectorAll('.cui-nav-link[data-cui-title]').forEach(function (element) {
            if (api.getInstance(element)) {
                return;
            }

            new api(element, {
                title: element.getAttribute('data-cui-title'),
                placement: 'right',
                trigger: 'hover focus',
                container: 'body'
            });
        });
    }

    function disableNavTooltips() {
        var api = tooltipApi();

        if (!api) {
            return;
        }

        document.querySelectorAll('.cui-nav-link[data-cui-title]').forEach(function (element) {
            var instance = api.getInstance(element);

            if (instance) {
                instance.dispose();
            }
        });
    }

    // Подсказки нужны только там, где пропадает текст: свёрнутая панель на десктопе.
    function syncNavTooltips() {
        if (isDesktop() && isCollapsed()) {
            enableNavTooltips();
        } else {
            disableNavTooltips();
        }
    }

    /* ===============================
       Восстановление состояния после навигации
       =============================== */

    // Усиленная навигация Blazor подставляет серверную разметку: класс body
    // со свёрнутой панелью пропадает, разделы раскрываются, а у новых ссылок
    // ещё нет подсказок. Наблюдаем за DOM и возвращаем состояние.
    function startMenuObserver() {
        if (typeof MutationObserver === 'undefined' || !document.body) {
            return;
        }

        var observer = new MutationObserver(function (mutations) {
            var syncNeeded = false;

            for (var i = 0; i < mutations.length; i++) {
                var mutation = mutations[i];

                if (mutation.type === 'attributes' && mutation.target === document.body) {
                    if (!isDesktop()) {
                        if (document.body.classList.contains(collapsedClass)) {
                            document.body.classList.remove(collapsedClass);
                        }
                    } else if (readStored() && !document.body.classList.contains(collapsedClass)) {
                        document.body.classList.add(collapsedClass);
                    }

                    syncNeeded = true;
                    continue;
                }

                if (mutation.type !== 'childList') {
                    continue;
                }

                var added = mutation.addedNodes;

                for (var j = 0; j < added.length; j++) {
                    var node = added[j];

                    if (node instanceof Element &&
                        (node.matches('.cui-nav-link[data-cui-title]') ||
                            node.querySelector('.cui-nav-link[data-cui-title]') ||
                            node.matches('[data-cui-section-key]') ||
                            node.querySelector('[data-cui-section-key]'))) {
                        syncNeeded = true;
                        break;
                    }
                }
            }

            if (syncNeeded) {
                restoreSections();
                syncNavTooltips();
            }
        });

        observer.observe(document.body, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: ['class']
        });
    }

    /* ===============================
       Клики
       =============================== */

    function closeOffcanvasFor(link) {
        var offcanvas = link.closest('.offcanvas');

        if (!offcanvas || !window.bootstrap || !window.bootstrap.Offcanvas) {
            return;
        }

        var instance = window.bootstrap.Offcanvas.getInstance(offcanvas);

        if (instance) {
            instance.hide();
        }
    }

    function onClick(event) {
        var target = event.target instanceof Element ? event.target : null;

        if (!target) {
            return;
        }

        if (target.closest('[data-cui-sidebar-toggle]')) {
            event.preventDefault();
            userToggled = true;
            setCollapsed(!isCollapsed(), true);
            return;
        }

        var link = target.closest('.offcanvas .cui-nav-link');

        if (link) {
            closeOffcanvasFor(link);
        }
    }

    document.addEventListener('click', onClick);

    function onEnhancedNavigation() {
        restoreCollapsedState();
        restoreSections();
        syncNavTooltips();
    }

    // В разных версиях Blazor событие усиленной навигации называется по-разному,
    // поэтому подписываемся на оба имени, а наблюдатель за DOM служит страховкой.
    document.addEventListener('enhancedload', onEnhancedNavigation);
    document.addEventListener('enhanced-navigation:load', onEnhancedNavigation);

    // Bootstrap может загружаться позже нашего скрипта (гибридные приложения).
    window.addEventListener('load', syncNavTooltips);

    if (typeof desktopQuery.addEventListener === 'function') {
        desktopQuery.addEventListener('change', onBreakpointChange);
    } else if (typeof desktopQuery.addListener === 'function') {
        desktopQuery.addListener(onBreakpointChange);
    }

    function init() {
        applyInitialState();
        restoreSections();
        syncNavTooltips();
        startMenuObserver();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init, { once: true });
    } else {
        init();
    }
})();
