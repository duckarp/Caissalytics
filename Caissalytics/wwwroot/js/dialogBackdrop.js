// Dialog backdrops close their dialog on click. A click is delivered to the nearest element containing
// both the press and the release, so pressing inside a dialog (e.g. selecting text in a field) and
// releasing over the backdrop "clicks" the backdrop and closes the dialog. Let a backdrop click through
// only when the press also started on the backdrop. This runs in the capture phase on document, before
// Blazor's own document-level (bubble phase) click handling, so it covers every dialog.
(function () {
	"use strict";

	var BACKDROPS = ".modal-backdrop, .rep-modal-backdrop, .board-editor-modal-backdrop, .fetch-modal-overlay";
	var pressTarget = null;

	document.addEventListener("mousedown", function (e) {
		pressTarget = e.target;
	}, true);

	document.addEventListener("click", function (e) {
		var target = e.target;
		if (target && target.matches && target.matches(BACKDROPS) && pressTarget !== target) {
			e.stopPropagation();
		}
	}, true);
})();
