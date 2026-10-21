window.notationInterop = {
    scrollMoveIntoView: function (elementId) {
        if (!elementId) return;
        const el = document.getElementById(elementId);
        if (el) {
            el.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'nearest' });
        }
    },

    _listeners: new Map(),
    _keyboardHandler: null,
    // Id of the visible workspace tab (null until the page reports it). Hidden tabs stay mounted
    // and keep their listeners, so keys are only claimed when the visible tab has one.
    _activeTabId: null,

    setActiveTab: function (tabId) {
        this._activeTabId = tabId || null;
    },

    _targetListeners: function () {
        if (this._activeTabId === null) return Array.from(this._listeners.values());
        const helper = this._listeners.get(this._activeTabId);
        return helper ? [helper] : [];
    },

    _ensureGlobalListener: function () {
        if (this._keyboardHandler) return;
        const self = this;
        this._altHeld = false;
        this._notifyAlt = function(held) {
            if (self._altHeld === held) return;
            self._altHeld = held;
            for (const helper of self._listeners.values()) {
                helper.invokeMethodAsync('OnGlobalAltChanged', held).catch(() => {});
            }
        };
        this._keyUpHandler = function(e) { if (!e.altKey) self._notifyAlt(false); };
        this._blurHandler = function() { self._notifyAlt(false); };
        window.addEventListener('keyup', this._keyUpHandler);
        window.addEventListener('blur', this._blurHandler);
        this._keyboardHandler = function (e) {
            if (self._listeners.size === 0) return;
            self._notifyAlt(e.altKey);

            // Let tabs without board shortcuts (database, settings, homework...) scroll normally
            const targets = self._targetListeners();
            if (targets.length === 0) return;

            // Ignore if active element is an input, textarea, select, or contenteditable
            const tag = (document.activeElement && document.activeElement.tagName) ? document.activeElement.tagName.toUpperCase() : '';
            if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || (document.activeElement && document.activeElement.isContentEditable)) {
                return;
            }

            // Only intercept shortcut-relevant keys
            const keys = ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', 'Space', ' '];
            const isLetterOrDigit = (e.key && e.key.length === 1);
            if (!keys.includes(e.key) && !isLetterOrDigit && !e.ctrlKey) {
                return;
            }

            // Prevent default scrolling for arrow keys, home, end, space, or default window close for Ctrl+W
            if (keys.includes(e.key) ||
                (e.ctrlKey && (e.key === 'w' || e.key === 'W')) ||
                (!e.ctrlKey && !e.altKey && (e.key === 'f' || e.key === 'F' || e.key === 'l' || e.key === 'L'))) {
                e.preventDefault();
            }

            const payload = {
                code: e.code,
                key: e.key,
                ctrlKey: e.ctrlKey || e.metaKey,
                altKey: e.altKey,
                shiftKey: e.shiftKey
            };

            for (const helper of targets) {
                // A listener being disposed rejects asynchronously; try/catch can't see that
                helper.invokeMethodAsync('OnGlobalKeyDown', payload).catch(() => { });
            }
        };
        window.addEventListener('keydown', this._keyboardHandler);
    },

    registerKeyboardListener: function (dotNetHelper, listenerId) {
        const key = listenerId || 'default';
        this._listeners.set(key, dotNetHelper);
        this._ensureGlobalListener();
    },

    unregisterKeyboardListener: function (listenerId) {
        if (listenerId) {
            this._listeners.delete(listenerId);
        } else {
            this._listeners.clear();
        }
        if (this._listeners.size === 0 && this._keyboardHandler) {
            window.removeEventListener('keydown', this._keyboardHandler);
            window.removeEventListener('keyup', this._keyUpHandler);
            window.removeEventListener('blur', this._blurHandler);
            this._keyboardHandler = null;
        }
    }
};
