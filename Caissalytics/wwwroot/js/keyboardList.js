// Keyboard navigation for lists and tables (used by KeyboardList.razor). Offline, no dependencies.
// The decision to claim a key has to be made synchronously (to stop arrows scrolling the page), so it
// happens here; the .NET side gets the action name (and whether Shift was held) and moves the active
// row, extends the selection or runs the row action.
(function () {
	"use strict";

	var NAVIGATION = { ArrowUp: "up", ArrowDown: "down", Home: "first", End: "last", PageUp: "pageUp", PageDown: "pageDown" };
	var ACTIONS = { Enter: "open", F2: "edit", Delete: "delete", " ": "toggle", Insert: "add" };
	var lists = {};
	var nextId = 1;

	// Keys typed into a control inside the list (note field, row buttons, drag handle...) belong to that control
	function isInnerControl(target, container) {
		if (target === container) return false;
		if (target.isContentEditable) return true;
		var tag = target.tagName;
		if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || tag === "BUTTON" || tag === "A") return true;
		return target.hasAttribute("tabindex");
	}

	window.keyboardList = {
		attach: function (container, dotNetRef, actions) {
			var id = nextId++;
			var entry = { el: container, ref: dotNetRef, actions: actions || {} };
			entry.handler = function (e) {
				if (e.defaultPrevented || e.altKey) return;
				if (isInnerControl(e.target, container)) return;

				var name = null;
				if (e.ctrlKey || e.metaKey) {
					// Only Ctrl+A (select all) is ours; other Ctrl shortcuts belong to the app
					if ((e.key === "a" || e.key === "A") && entry.actions.select) name = "selectAll";
				} else if (e.key === "Escape") {
					if (entry.actions.select) name = "clearSelection";
				} else {
					name = NAVIGATION[e.key];
					if (!name && entry.actions[ACTIONS[e.key]]) name = ACTIONS[e.key];
				}
				if (!name) return;

				e.preventDefault();
				// Keep window-level shortcut handlers (e.g. board navigation) from also acting on it
				e.stopPropagation();
				entry.ref.invokeMethodAsync("OnListKey", name, e.shiftKey).catch(function () { });
			};
			// Shift+click selects a range of rows; without this the browser would also select page text
			entry.mouseHandler = function (e) {
				if (e.shiftKey && entry.actions.select && !isInnerControl(e.target, container)) {
					e.preventDefault();
					container.focus({ preventScroll: true });
				}
			};
			container.addEventListener("keydown", entry.handler);
			container.addEventListener("mousedown", entry.mouseHandler);
			lists[id] = entry;
			return id;
		},

		setActions: function (id, actions) {
			if (lists[id]) lists[id].actions = actions || {};
		},

		scrollActiveIntoView: function (container) {
			var row = container && container.querySelector(".kl-row-active");
			if (row) row.scrollIntoView({ block: "nearest" });
		},

		focus: function (container) {
			if (container) container.focus({ preventScroll: true });
		},

		detach: function (id) {
			var entry = lists[id];
			if (!entry) return;
			entry.el.removeEventListener("keydown", entry.handler);
			entry.el.removeEventListener("mousedown", entry.mouseHandler);
			delete lists[id];
		}
	};
})();
