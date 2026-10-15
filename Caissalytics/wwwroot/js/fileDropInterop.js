// File drop interop for Caissalytics (supports PGN, ZIP, 7Z, etc.)

// Prevent default browser drag/drop behavior on the entire window (e.g. navigating away to dropped file)
if (typeof window !== 'undefined' && !window._caissalyticsFileDropInitialized) {
    window._caissalyticsFileDropInitialized = true;
    window.addEventListener('dragover', (e) => {
        if (hasFiles(e)) {
            e.preventDefault();
        }
    });
    window.addEventListener('drop', (e) => {
        if (hasFiles(e)) {
            e.preventDefault();
        }
    });
}

function hasFiles(e) {
    if (!e.dataTransfer || !e.dataTransfer.types) return false;
    const types = Array.from(e.dataTransfer.types);
    return types.some(t => {
        const lower = t.toLowerCase();
        return lower === 'files' || lower.includes('uri') || lower.includes('file');
    });
}

function extractLocalPath(dataTransfer) {
    if (!dataTransfer) return null;
    const uriList = dataTransfer.getData('text/uri-list') || dataTransfer.getData('text/plain');
    if (!uriList) return null;
    const lines = uriList.split(/[\r\n]+/).map(l => l.trim()).filter(l => l && !l.startsWith('#'));
    if (lines.length === 0) return null;
    const first = lines[0];
    if (first.startsWith('file://')) {
        try {
            return decodeURIComponent(new URL(first).pathname);
        } catch {
            return decodeURIComponent(first.replace(/^file:\/\//, ''));
        }
    }
    if (first.startsWith('/')) {
        return first;
    }
    return null;
}

export function registerDropZone(element, dotNetHelper, methodName, acceptedExtensions, options = { mode: 'text' }) {
    if (!element) return null;

    let dragCounter = 0;

    function onDragEnter(e) {
        if (!hasFiles(e)) return;
        e.preventDefault();
        e.stopPropagation();
        dragCounter++;
        if (e.dataTransfer) {
            e.dataTransfer.dropEffect = 'copy';
        }
        element.classList.add('drag-active');
    }

    function onDragOver(e) {
        if (!hasFiles(e)) return;
        e.preventDefault();
        e.stopPropagation();
        if (e.dataTransfer) {
            e.dataTransfer.dropEffect = 'copy';
        }
        element.classList.add('drag-active');
    }

    function onDragLeave(e) {
        if (!hasFiles(e)) return;
        e.preventDefault();
        e.stopPropagation();
        dragCounter--;
        if (dragCounter <= 0) {
            dragCounter = 0;
            element.classList.remove('drag-active');
        }
    }

    async function onDrop(e) {
        if (!hasFiles(e)) return;
        e.preventDefault();
        e.stopPropagation();
        dragCounter = 0;
        element.classList.remove('drag-active');

        // Check if a direct local file path is available (standard in Linux WebKitGTK)
        const localPath = extractLocalPath(e.dataTransfer);
        if (localPath && dotNetHelper) {
            const ext = '.' + localPath.split('.').pop().toLowerCase();
            if (acceptedExtensions && acceptedExtensions.length > 0 && !acceptedExtensions.includes(ext)) {
                try {
                    await dotNetHelper.invokeMethodAsync('OnFileDropError', `Unsupported file type "${localPath.split('/').pop()}". Supported formats: ${acceptedExtensions.join(', ')}`);
                } catch { }
                return;
            }

            try {
                const pathMethod = methodName.endsWith('Path') ? methodName : (methodName + 'Path');
                await dotNetHelper.invokeMethodAsync(pathMethod, localPath);
                return;
            } catch (err) {
                console.warn('[fileDropInterop] Direct path callback failed, falling back to File object:', err);
            }
        }

        const files = e.dataTransfer?.files;
        if (!files || files.length === 0) return;

        const file = files[0];
        const dotIndex = file.name.lastIndexOf('.');
        const ext = dotIndex !== -1 ? file.name.substring(dotIndex).toLowerCase() : '';

        if (acceptedExtensions && acceptedExtensions.length > 0) {
            const isAccepted = acceptedExtensions.some(a => a.toLowerCase() === ext);
            if (!isAccepted) {
                if (dotNetHelper) {
                    try {
                        await dotNetHelper.invokeMethodAsync('OnFileDropError', `Unsupported file type "${file.name}". Supported formats: ${acceptedExtensions.join(', ')}`);
                    } catch { }
                }
                return;
            }
        }

        // 1. Attempt populating nested InputFile as progressive enhancement
        const input = element.querySelector('input[type="file"]');
        if (input) {
            try {
                const dt = new DataTransfer();
                for (let i = 0; i < files.length; i++) {
                    dt.items.add(files[i]);
                }
                input.files = dt.files;
                input.dispatchEvent(new Event('change', { bubbles: true }));
            } catch (err) {
                // In WebKit / Photino this may throw or be ignored
            }
        }

        // 2. Direct DotNet callback
        if (dotNetHelper && methodName) {
            try {
                if (options && options.mode === 'stream') {
                    let streamRef = null;
                    if (window.DotNet && typeof window.DotNet.createJSStreamReference === 'function') {
                        streamRef = window.DotNet.createJSStreamReference(file);
                    }
                    await dotNetHelper.invokeMethodAsync(methodName, file.name, file.size, streamRef);
                } else {
                    const text = await file.text();
                    await dotNetHelper.invokeMethodAsync(methodName, file.name, text);
                }
            } catch (err) {
                console.error('[fileDropInterop] Error invoking drop callback:', err);
                try {
                    await dotNetHelper.invokeMethodAsync('OnFileDropError', `Error reading dropped file: ${err.message || err}`);
                } catch { }
            }
        }
    }

    element.addEventListener('dragenter', onDragEnter);
    element.addEventListener('dragover', onDragOver);
    element.addEventListener('dragleave', onDragLeave);
    element.addEventListener('drop', onDrop);

    return {
        dispose: () => {
            element.removeEventListener('dragenter', onDragEnter);
            element.removeEventListener('dragover', onDragOver);
            element.removeEventListener('dragleave', onDragLeave);
            element.removeEventListener('drop', onDrop);
        }
    };
}

export function registerWorkbenchDrop(workbenchEl, dotNetHelper, methodName, acceptedExtensions = ['.pgn'], options = { mode: 'text' }) {
    if (!workbenchEl) return null;

    let dragCounter = 0;
    const overlay = workbenchEl.querySelector('.workbench-drag-drop-overlay');

    function onDragEnter(e) {
        if (!hasFiles(e)) return;
        e.preventDefault();
        e.stopPropagation();
        dragCounter++;
        if (e.dataTransfer) {
            e.dataTransfer.dropEffect = 'copy';
        }
        if (overlay) overlay.classList.add('visible');
    }

    function onDragOver(e) {
        if (!hasFiles(e)) return;
        e.preventDefault();
        e.stopPropagation();
        if (e.dataTransfer) {
            e.dataTransfer.dropEffect = 'copy';
        }
        if (overlay) overlay.classList.add('visible');
    }

    function onDragLeave(e) {
        if (!hasFiles(e)) return;
        e.preventDefault();
        e.stopPropagation();
        dragCounter--;
        if (dragCounter <= 0) {
            dragCounter = 0;
            if (overlay) overlay.classList.remove('visible');
        }
    }

    async function onDrop(e) {
        if (!hasFiles(e)) return;
        e.preventDefault();
        e.stopPropagation();
        dragCounter = 0;
        if (overlay) overlay.classList.remove('visible');

        // Check for direct local file path (Linux WebKitGTK)
        const localPath = extractLocalPath(e.dataTransfer);
        if (localPath && dotNetHelper) {
            const ext = '.' + localPath.split('.').pop().toLowerCase();
            if (acceptedExtensions && acceptedExtensions.length > 0 && !acceptedExtensions.includes(ext)) {
                return;
            }
            try {
                const pathMethod = methodName.endsWith('Path') ? methodName : (methodName + 'Path');
                await dotNetHelper.invokeMethodAsync(pathMethod, localPath);
                return;
            } catch (err) {
                console.warn('[fileDropInterop] Workbench path callback failed, trying File object:', err);
            }
        }

        const files = e.dataTransfer?.files;
        if (!files || files.length === 0) return;

        const file = files[0];
        const dotIndex = file.name.lastIndexOf('.');
        const ext = dotIndex !== -1 ? file.name.substring(dotIndex).toLowerCase() : '';

        if (acceptedExtensions && acceptedExtensions.length > 0) {
            const isAccepted = acceptedExtensions.some(a => a.toLowerCase() === ext);
            if (!isAccepted) {
                return;
            }
        }

        try {
            if (options && options.mode === 'stream') {
                let streamRef = null;
                if (window.DotNet && typeof window.DotNet.createJSStreamReference === 'function') {
                    streamRef = window.DotNet.createJSStreamReference(file);
                }
                if (dotNetHelper && methodName) {
                    await dotNetHelper.invokeMethodAsync(methodName, file.name, file.size, streamRef);
                }
            } else {
                const text = await file.text();
                if (dotNetHelper && methodName) {
                    await dotNetHelper.invokeMethodAsync(methodName, file.name, text);
                }
            }
        } catch (err) {
            console.error('[fileDropInterop] Error reading workbench dropped file:', err);
        }
    }

    workbenchEl.addEventListener('dragenter', onDragEnter);
    workbenchEl.addEventListener('dragover', onDragOver);
    workbenchEl.addEventListener('dragleave', onDragLeave);
    workbenchEl.addEventListener('drop', onDrop);

    return {
        dispose: () => {
            workbenchEl.removeEventListener('dragenter', onDragEnter);
            workbenchEl.removeEventListener('dragover', onDragOver);
            workbenchEl.removeEventListener('dragleave', onDragLeave);
            workbenchEl.removeEventListener('drop', onDrop);
        }
    };
}
