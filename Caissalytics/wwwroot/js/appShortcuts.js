// App-wide shortcuts that must work on every tab (registered by WorkspaceTabs.razor): Close Tab.
// Board tabs used to handle it through their own keyboard listeners, so it did nothing on tabs without
// a board (Settings, Database, ...). Matching mirrors ShortcutBinding.Matches; Cmd counts as Ctrl.
(function () {
	"use strict";

	var entry = null;

	function keyMatches(bindingKey, key) {
		if (!bindingKey || !key) return false;
		var b = bindingKey.toLowerCase();
		if (b === " " || b === "space") return key === " " || key.toLowerCase() === "space";
		return b === key.toLowerCase();
	}

	function matches(binding, e) {
		return keyMatches(binding.key, e.key)
			&& binding.ctrl === (e.ctrlKey || e.metaKey)
			&& binding.alt === e.altKey
			&& binding.shift === e.shiftKey;
	}

	window.appShortcuts = {
		register: function (dotNetRef, closeTabBindings) {
			this.unregister();
			entry = { ref: dotNetRef, closeTab: closeTabBindings || [] };
			entry.handler = function (e) {
				// Keys pressed while recording a new shortcut in Settings belong to the recorder
				if (e.target && e.target.closest && e.target.closest("[data-shortcut-recorder]")) return;
				for (var i = 0; i < entry.closeTab.length; i++) {
					if (!matches(entry.closeTab[i], e)) continue;
					e.preventDefault();
					// Holding the keys down must not close one tab after another
					if (!e.repeat) entry.ref.invokeMethodAsync("OnCloseTabShortcut").catch(function () { });
					return;
				}
			};
			window.addEventListener("keydown", entry.handler);
		},

		setCloseTabBindings: function (closeTabBindings) {
			if (entry) entry.closeTab = closeTabBindings || [];
		},

		unregister: function () {
			if (!entry) return;
			window.removeEventListener("keydown", entry.handler);
			entry = null;
		}
	};
})();
