// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The Web UI's only script of its own, loaded from the app's origin so the
// CSP needs no 'unsafe-inline' (ADR 0202). It sets data-theme before the
// first paint (docs/ui/README.md, Implementing): from the viewer's stored
// choice, or else prefers-color-scheme, following the media query while no
// choice is stored. And it holds the calls the components and pages make that
// .NET cannot: the clipboard, a modal dialog, the query editor's keys and
// frames (ADR 0250), a download saved as a file, and the tab's storage.
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
        var bar = document.querySelector('.vw-shell__bar');

        if (event.key === 'Escape' && bar) {
            bar.classList.add('vw-quiet');
        }
    });

    function wake(event) {
        var target = event.target instanceof Element ? event.target.closest('.vw-shell__who, .vw-plane') : null;
        var bar = target ? target.closest('.vw-shell__bar') : null;

        if (bar) {
            bar.classList.remove('vw-quiet');
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


    // An open Menu (docs/ui/components/Menu): Escape closes it and returns
    // focus to its button. The button's own click closes it, so .NET keeps
    // the state.
    document.addEventListener('keydown', function (event) {
        var menu = event.key === 'Escape' && event.target instanceof Element ? event.target.closest('.vw-menu') : null;
        var button = menu ? menu.querySelector(':scope > button[aria-expanded="true"]') : null;

        if (button) {
            button.click();
            button.focus();
        }
    });

    // The query editor (ADR 0250): the browser edits; this reports the text
    // and the caret to the page at most once a frame, so a burst of typing is
    // coloured once. Tab inserts two spaces and Shift+Tab removes up to two at
    // the line's start; Escape, then Tab, leaves the editor (WCAG 2.1.2).
    function editor(id, page) {
        var textarea = document.getElementById(id);
        var frame = 0;
        var leaving = false;

        function report() {
            if (!frame) {
                frame = window.requestAnimationFrame(function () {
                    frame = 0;
                    page.invokeMethodAsync('Edited', textarea.value, textarea.selectionStart);
                });
            }
        }

        function edited() {
            textarea.dispatchEvent(new Event('input', { bubbles: true }));
        }

        function keydown(event) {
            if (event.key === 'Escape') {
                leaving = true;
                return;
            }

            if (event.key !== 'Tab' || leaving || textarea.readOnly) {
                leaving = event.key === 'Tab' ? false : leaving && event.key === 'Shift';
                return;
            }

            event.preventDefault();
            var start = textarea.selectionStart;
            var end = textarea.selectionEnd;

            if (!event.shiftKey) {
                textarea.setRangeText('  ', start, end, 'end');
                edited();
                return;
            }

            var line = textarea.value.lastIndexOf('\n', start - 1) + 1;
            var spaces = textarea.value.slice(line, line + 2).match(/^ {0,2}/)[0].length;

            if (spaces > 0) {
                textarea.setRangeText('', line, line + spaces, 'preserve');
                textarea.setSelectionRange(Math.max(line, start - spaces), Math.max(line, end - spaces));
                edited();
            }
        }

        function caret() {
            if (document.activeElement === textarea) {
                report();
            }
        }

        textarea.addEventListener('input', report);
        textarea.addEventListener('keydown', keydown);
        document.addEventListener('selectionchange', caret);
        return {
            set: function (text) {
                textarea.value = text;
                textarea.setSelectionRange(text.length, text.length);
                report();
            },
            dispose: function () {
                window.cancelAnimationFrame(frame);
                textarea.removeEventListener('input', report);
                textarea.removeEventListener('keydown', keydown);
                document.removeEventListener('selectionchange', caret);
            },
        };
    }

    // A download (ADR 0254 point 5): .NET hands over the body a chunk at a
    // time as it arrives, and never holds it whole; the chunks become one
    // Blob, saved under the name given.
    var downloads = {};
    var nextDownload = 0;
    var historyKey = 'varve.query-history';

    window.varveWebUi = {
        editor: editor,
        downloadBegin: function () {
            nextDownload += 1;
            downloads[nextDownload] = [];
            return nextDownload;
        },
        downloadAppend: function (id, bytes) {
            downloads[id].push(new Blob([bytes]));
        },
        downloadSave: function (id, name, type) {
            var url = URL.createObjectURL(new Blob(downloads[id], { type: type }));
            var link = document.createElement('a');
            delete downloads[id];
            link.href = url;
            link.download = name;
            document.body.appendChild(link);
            link.click();
            link.remove();
            window.setTimeout(function () { URL.revokeObjectURL(url); }, 0);
        },
        downloadDrop: function (id) {
            delete downloads[id];
        },
        // The query history (ADR 0254 point 7): this tab only, and cleared at
        // sign-out. No result, token, position read or request id is in it.
        historyRead: function () {
            try {
                return window.sessionStorage.getItem(historyKey);
            } catch (e) {
                return null;
            }
        },
        historyWrite: function (json) {
            try {
                window.sessionStorage.setItem(historyKey, json);
            } catch (e) {
                // Without storage, the history is this page's only.
            }
        },
        historyClear: function () {
            try {
                window.sessionStorage.removeItem(historyKey);
            } catch (e) {
                // Nothing was stored.
            }
        },
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
