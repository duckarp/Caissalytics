window.notationInterop = {
    scrollMoveIntoView: function (elementId) {
        if (!elementId) return;
        const el = document.getElementById(elementId);
        if (el) {
            el.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'nearest' });
        }
    },

    _keyboardHandler: null,

    registerKeyboardListener: function (dotNetHelper) {
        if (this._keyboardHandler) {
            window.removeEventListener('keydown', this._keyboardHandler);
        }

        this._keyboardHandler = function (e) {
            // Ignore if active element is an input, textarea, select, or contenteditable
            const tag = (document.activeElement && document.activeElement.tagName) ? document.activeElement.tagName.toUpperCase() : '';
            if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || (document.activeElement && document.activeElement.isContentEditable)) {
                return;
            }

            // Only intercept shortcut-relevant keys
            const keys = ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', 'Space'];
            if (!keys.includes(e.key) && !e.ctrlKey) {
                return;
            }

            // Prevent default scrolling for arrow keys, home, end, or default window close for Ctrl+W
            if (['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', ' '].includes(e.key) ||
                (e.ctrlKey && (e.key === 'w' || e.key === 'W'))) {
                e.preventDefault();
            }

            dotNetHelper.invokeMethodAsync('OnGlobalKeyDown', {
                key: e.key,
                ctrlKey: e.ctrlKey || e.metaKey,
                altKey: e.altKey,
                shiftKey: e.shiftKey
            });
        };

        window.addEventListener('keydown', this._keyboardHandler);
    },

    unregisterKeyboardListener: function () {
        if (this._keyboardHandler) {
            window.removeEventListener('keydown', this._keyboardHandler);
            this._keyboardHandler = null;
        }
    }
};
