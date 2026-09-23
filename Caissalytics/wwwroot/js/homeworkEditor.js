// Caissalytics Homework & Coaching Notes Editor Interop
(function () {
    window.homeworkEditor = {
        formatSelection: function (textareaId, prefix, suffix) {
            suffix = suffix || '';
            var textarea = document.getElementById(textareaId);
            if (!textarea) return null;

            var start = textarea.selectionStart;
            var end = textarea.selectionEnd;
            var value = textarea.value || '';

            var selectedText = value.substring(start, end);
            var replacement = '';

            if (selectedText.length > 0) {
                // Wrap the selected text
                replacement = prefix + selectedText + suffix;
                textarea.value = value.substring(0, start) + replacement + value.substring(end);
                textarea.selectionStart = start;
                textarea.selectionEnd = start + replacement.length;
            } else {
                // No text selected: insert template and highlight placeholder
                var placeholder = suffix ? 'text' : '';
                replacement = prefix + placeholder + suffix;
                textarea.value = value.substring(0, start) + replacement + value.substring(end);
                if (placeholder) {
                    textarea.selectionStart = start + prefix.length;
                    textarea.selectionEnd = start + prefix.length + placeholder.length;
                } else {
                    textarea.selectionStart = textarea.selectionEnd = start + replacement.length;
                }
            }

            textarea.focus();
            textarea.dispatchEvent(new Event('input', { bubbles: true }));
            return textarea.value;
        },

        formatLinePrefix: function (textareaId, linePrefix) {
            var textarea = document.getElementById(textareaId);
            if (!textarea) return null;

            var start = textarea.selectionStart;
            var end = textarea.selectionEnd;
            var value = textarea.value || '';

            // Find line bounds
            var lineStart = value.lastIndexOf('\n', start - 1) + 1;
            var lineEnd = value.indexOf('\n', end);
            if (lineEnd === -1) lineEnd = value.length;

            var selectedBlock = value.substring(lineStart, lineEnd);
            var lines = selectedBlock.split('\n');

            // Toggle or prepend prefix to lines
            var newLines = lines.map(function (line) {
                return linePrefix + line;
            }).join('\n');

            textarea.value = value.substring(0, lineStart) + newLines + value.substring(lineEnd);
            textarea.selectionStart = lineStart;
            textarea.selectionEnd = lineStart + newLines.length;

            textarea.focus();
            textarea.dispatchEvent(new Event('input', { bubbles: true }));
            return textarea.value;
        }
    };
})();
