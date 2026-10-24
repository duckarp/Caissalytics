// Right-click menus (used by ContextMenu.razor). Offline, no dependencies.
// Keeps the open menu inside the window, moves focus with the arrow keys, and closes it on Escape, a
// press outside it, scrolling, resizing or the window losing focus. The Menu key and Shift+F10 open a
// menu from the keyboard, anchored to the active row of the focused list.
(function () {
	"use strict";

	var MARGIN = 4;
	var current = null;
	var watchers = {};
	var nextWatchId = 1;

	function enabledItems(menu) {
		return Array.prototype.slice.call(menu.querySelectorAll(".ctx-menu-item:not(:disabled)"));
	}

	function moveFocus(menu, step) {
		var items = enabledItems(menu);
		if (items.length === 0) return;
		var i = items.indexOf(document.activeElement);
		var next;
		if (step === "first") next = 0;
		else if (step === "last") next = items.length - 1;
		else if (i < 0) next = step > 0 ? 0 : items.length - 1;
		else next = (i + step + items.length) % items.length;
		items[next].focus({ preventScroll: true });
	}

	function close(restoreFocus, notify) {
		var c = current;
		if (!c) return;
		current = null;
		document.removeEventListener("mousedown", c.onPress, true);
		window.removeEventListener("scroll", c.onScroll, true);
		window.removeEventListener("resize", c.onDismiss);
		window.removeEventListener("blur", c.onDismiss);
		c.menu.removeEventListener("keydown", c.onKey);
		if (restoreFocus && c.returnFocus && document.contains(c.returnFocus)) {
			c.returnFocus.focus({ preventScroll: true });
		}
		if (notify) c.ref.invokeMethodAsync("OnDismissed", c.generation).catch(function () { });
	}

	window.contextMenu = {
		show: function (menu, dotNetRef, generation, x, y, focusFirst) {
			if (!menu) return;
			// Reopening (e.g. right-clicking another row) keeps the element the first menu was opened from
			var returnFocus = current ? current.returnFocus : document.activeElement;
			close(false, false);

			// Open to the left of / above the pointer when there is no room to the right / below
			var w = menu.offsetWidth, h = menu.offsetHeight;
			if (x + w > window.innerWidth - MARGIN) x = Math.max(MARGIN, x - w);
			if (y + h > window.innerHeight - MARGIN) y = Math.max(MARGIN, y - h);
			menu.style.left = x + "px";
			menu.style.top = y + "px";
			menu.style.visibility = "visible";

			var c = { menu: menu, ref: dotNetRef, generation: generation, returnFocus: returnFocus };
			c.onPress = function (e) {
				if (!menu.contains(e.target)) close(false, true);
			};
			c.onScroll = function (e) {
				if (!menu.contains(e.target)) close(false, true);
			};
			c.onDismiss = function () {
				close(false, true);
			};
			c.onKey = function (e) {
				// The menu owns the keyboard while open: no list or app shortcuts underneath
				e.stopPropagation();
				switch (e.key) {
					case "ArrowDown": e.preventDefault(); moveFocus(menu, 1); break;
					case "ArrowUp": e.preventDefault(); moveFocus(menu, -1); break;
					case "Home": e.preventDefault(); moveFocus(menu, "first"); break;
					case "End": e.preventDefault(); moveFocus(menu, "last"); break;
					case "Escape": e.preventDefault(); close(true, true); break;
					case "Tab": e.preventDefault(); break;
				}
			};
			document.addEventListener("mousedown", c.onPress, true);
			window.addEventListener("scroll", c.onScroll, true);
			window.addEventListener("resize", c.onDismiss);
			window.addEventListener("blur", c.onDismiss);
			menu.addEventListener("keydown", c.onKey);
			current = c;

			if (focusFirst) moveFocus(menu, "first");
			else menu.focus({ preventScroll: true });
		},

		hide: function (restoreFocus) {
			close(restoreFocus, false);
		},

		// Menu key / Shift+F10 pressed on an element inside scope matching triggerSelector opens the menu
		// at that element's descendant matching anchorSelector (or the element itself)
		watchKeys: function (scope, dotNetRef, triggerSelector, anchorSelector) {
			if (!scope) return 0;
			var id = nextWatchId++;
			var handler = function (e) {
				if (e.key !== "ContextMenu" && !(e.key === "F10" && e.shiftKey)) return;
				var target = e.target;
				if (!target || !target.matches || !target.matches(triggerSelector)) return;
				e.preventDefault();
				e.stopPropagation();
				var anchor = (anchorSelector && target.querySelector(anchorSelector)) || target;
				var rect = anchor.getBoundingClientRect();
				dotNetRef.invokeMethodAsync("OnKeyboardTrigger", rect.left + Math.min(40, rect.width / 2), rect.bottom).catch(function () { });
			};
			scope.addEventListener("keydown", handler);
			watchers[id] = { el: scope, handler: handler };
			return id;
		},

		unwatchKeys: function (id) {
			var w = watchers[id];
			if (!w) return;
			w.el.removeEventListener("keydown", w.handler);
			delete watchers[id];
		}
	};
})();
