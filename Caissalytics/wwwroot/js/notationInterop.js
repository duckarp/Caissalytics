window.notationInterop = {
    scrollMoveIntoView: function (elementId) {
        if (!elementId) return;
        const el = document.getElementById(elementId);
        if (el) {
            el.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'nearest' });
        }
    }
};
