// Language and theme preferences for the app shell.
//
// Loaded as a blocking script in <head>, before anything paints and long
// before the .NET runtime is downloaded. That ordering is the whole point:
// resolving the theme from Blazor would mean rendering a light page first and
// repainting it dark once WebAssembly boots, which is exactly the flash a dark
// mode is supposed to avoid. The same applies to the loading text and <title>,
// which are on screen for the seconds the runtime takes to arrive.
//
// Values are written to localStorage as plain strings rather than through
// Blazored.LocalStorage, because this file has to read them without any .NET
// involvement and JSON-quoted values would not survive that.
window.mftPrefs = (function () {
    'use strict';

    var THEME_KEY = 'mft.theme';
    var CULTURE_KEY = 'mft.culture';

    var THEMES = ['light', 'dark'];
    var CULTURES = ['es', 'en'];

    // Only the static text baked into index.html. Everything rendered by
    // Blazor comes from the .resx files instead.
    var SHELL = {
        es: {
            title: 'Mi Futuro Trabajo',
            loading: '"Cargando\u2026"',
            error: 'Se produjo un error inesperado.',
            reload: 'Recargar',
            boundary: '"Se produjo un error."'
        },
        en: {
            title: 'Mi Futuro Trabajo',
            loading: '"Loading\u2026"',
            error: 'An unhandled error has occurred.',
            reload: 'Reload',
            boundary: '"An error has occurred."'
        }
    };

    function read(key) {
        try {
            return window.localStorage.getItem(key);
        } catch (e) {
            // Private mode, or storage disabled by policy. Not being able to
            // remember a preference is not a reason to fail to render.
            return null;
        }
    }

    function write(key, value) {
        try {
            window.localStorage.setItem(key, value);
        } catch (e) {
            /* see read() */
        }
    }

    function oneOf(value, allowed) {
        return allowed.indexOf(value) >= 0 ? value : null;
    }

    function detectTheme() {
        return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches
            ? 'dark'
            : 'light';
    }

    function detectCulture() {
        var tags = window.navigator.languages || [window.navigator.language || ''];

        for (var i = 0; i < tags.length; i++) {
            var tag = String(tags[i]).toLowerCase();
            if (tag.indexOf('es') === 0) { return 'es'; }
            if (tag.indexOf('en') === 0) { return 'en'; }
        }

        // Neither language is in the browser's list, so fall back to the
        // application default rather than guessing.
        return 'es';
    }

    function resolveTheme() {
        return oneOf(read(THEME_KEY), THEMES) || detectTheme();
    }

    function resolveCulture() {
        return oneOf(read(CULTURE_KEY), CULTURES) || detectCulture();
    }

    function applyTheme(theme) {
        var value = oneOf(theme, THEMES) || 'light';
        var root = document.documentElement;

        // data-theme drives this application's own tokens; data-bs-theme is
        // what Bootstrap 5.3 reads for its built-in components. Both are set
        // so vendored Bootstrap and hand-written CSS never disagree.
        root.setAttribute('data-theme', value);
        root.setAttribute('data-bs-theme', value);

        return value;
    }

    function applyCulture(culture) {
        var value = oneOf(culture, CULTURES) || 'es';
        var text = SHELL[value];
        var root = document.documentElement;

        root.setAttribute('lang', value);
        root.style.setProperty('--mft-loading-text', text.loading);
        root.style.setProperty('--mft-error-boundary-text', text.boundary);
        document.title = text.title;

        var errorUi = document.getElementById('blazor-error-ui');
        if (errorUi) {
            var message = errorUi.querySelector('.message');
            var reload = errorUi.querySelector('.reload');
            if (message) { message.textContent = text.error; }
            if (reload) { reload.textContent = text.reload; }
        }

        return value;
    }

    function setTheme(theme) {
        var value = applyTheme(theme);
        write(THEME_KEY, value);
        return value;
    }

    function setCulture(culture) {
        var value = applyCulture(culture);
        write(CULTURE_KEY, value);
        return value;
    }

    // Apply before first paint. The DOM lookups inside applyCulture are
    // deferred because <body> does not exist yet at this point.
    var initialTheme = applyTheme(resolveTheme());
    var initialCulture = resolveCulture();

    document.documentElement.setAttribute('lang', initialCulture);
    document.documentElement.style.setProperty('--mft-loading-text', SHELL[initialCulture].loading);
    document.documentElement.style.setProperty('--mft-error-boundary-text', SHELL[initialCulture].boundary);

    document.addEventListener('DOMContentLoaded', function () {
        applyCulture(initialCulture);
    });

    return {
        getTheme: resolveTheme,
        getCulture: resolveCulture,
        setTheme: setTheme,
        setCulture: setCulture,
        initialTheme: initialTheme,
        initialCulture: initialCulture
    };
})();
