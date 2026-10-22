// Keyboard navigation for lists and tables (used by KeyboardList.razor). Offline, no dependencies.
// The decision to claim a key has to be made synchronously (to stop arrows scrolling the page), so it
// happens here; the .NET side gets the action name and moves the active row or runs the row action.
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
				if (e.defaultPrevented || e.ctrlKey || e.altKey || e.metaKey) return;
				if (isInnerControl(e.target, container)) return;

				var name = NAVIGATION[e.key];
				if (!name && entry.actions[ACTIONS[e.key]]) name = ACTIONS[e.key];
				if (!name) return;

				e.preventDefault();
				// Keep window-level shortcut handlers (e.g. board navigation) from also acting on it
				e.stopPropagation();
				entry.ref.invokeMethodAsync("OnListKey", name).catch(function () { });
			};
			container.addEventListener("keydown", entry.handler);
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
			delete lists[id];
		}
	};
})();
