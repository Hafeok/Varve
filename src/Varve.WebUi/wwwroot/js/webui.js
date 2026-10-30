// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The Web UI's only script of its own, loaded from the app's origin so the
// CSP needs no 'unsafe-inline' (ADR 0202). It sets data-theme before the
// first paint (docs/ui/README.md, Implementing): from the viewer's stored
// choice, or else prefers-color-scheme, following the media query while no
// choice is stored. And it holds the three calls the components make that
// .NET cannot: copy to the clipboard, and open and close a modal dialog.
(function () {
    'use strict';
    var key = 'varve.theme';
    var media = window.matchMedia('(prefers-color-scheme: dark)');

    function stored() {
        try {
            var choice = window.localStorage.getItem(key);
            return choice === 'light' || choice === 'dark' ? choice : null;
        } catch (e) {
            return null;
        }
    }

    function apply() {
        document.documentElement.setAttribute('data-theme', stored() || (media.matches ? 'dark' : 'light'));
    }

    apply();
    media.addEventListener('change', apply);

    // The roles shown on hover or focus of who is signed in (Shell): Escape
    // hides them without moving focus or the pointer (WCAG 1.4.13), until the
    // pointer or focus comes to a plane again.
    document.addEventListener('keydown', function (event) {
        if (event.key === 'Escape') {
            document.querySelectorAll('.vw-plane').forEach(function (plane) {
                plane.classList.add('vw-plane--quiet');
            });
        }
    });

    function wake(event) {
        var plane = event.target instanceof Element ? event.target.closest('.vw-plane') : null;

        if (plane) {
            plane.classList.remove('vw-plane--quiet');
        }
    }

    document.addEventListener('focusin', wake);
    document.addEventListener('mouseover', wake);

    // The skip link (Shell): its href is "#main", which against the base
    // address would be a navigation to the root. In the capture phase, before
    // Blazor's router sees the click, it moves focus to the main column instead.
    document.addEventListener('click', function (event) {
        var link = event.target instanceof Element ? event.target.closest('a.vw-skip') : null;
        var main = link ? document.getElementById('main') : null;

        if (main) {
            event.preventDefault();
            main.focus();
        }
    }, true);

    window.varveWebUi = {
        copy: function (text) {
            return navigator.clipboard.writeText(text);
        },
        showModal: function (dialog) {
            if (!dialog.open) {
                dialog.showModal();
            }
        },
        close: function (dialog) {
            if (dialog.open) {
                dialog.close();
            }
        },
        // The sign-in in flight (ADR 0201): state, nonce, verifier and return
        // path, never a token. Taken out as it is read, so a callback is
        // honoured once.
        sessionPut: function (key, value) {
            window.sessionStorage.setItem(key, value);
        },
        sessionTake: function (key) {
            var value = window.sessionStorage.getItem(key);
            window.sessionStorage.removeItem(key);
            return value;
        },
        // Back to the top of the main column, where the live tail runs.
        toTop: function () {
            var main = document.getElementById('main');

            if (main) {
                main.scrollTop = 0;
            }

            window.scrollTo(0, 0);
        },
        // Whether the main column is at its top, reported to the page on
        // every scroll (the live tail pauses while the operator reads lower).
        watchTop: function (page) {
            var main = document.getElementById('main');
            var last = null;

            function report() {
                var top = (main ? main.scrollTop : 0) <= 8 && window.scrollY <= 8;

                if (top !== last) {
                    last = top;
                    page.invokeMethodAsync('AtTop', top);
                }
            }

            if (main) {
                main.addEventListener('scroll', report, { passive: true });
            }

            window.addEventListener('scroll', report, { passive: true });
            report();
            return {
                dispose: function () {
                    if (main) {
                        main.removeEventListener('scroll', report);
                    }

                    window.removeEventListener('scroll', report);
                },
            };
        },
        setTheme: function (choice) {
            try {
                if (choice === 'light' || choice === 'dark') {
                    window.localStorage.setItem(key, choice);
                } else {
                    window.localStorage.removeItem(key);
                }
            } catch (e) {
                // A viewer's choice is a convenience: without storage, the media query decides.
            }

            apply();
        },
    };
})();
