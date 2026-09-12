// Accumulates PGN text parts on the browser side and triggers a file download.
// The PGN is built in chunks on the C# side to keep memory bounded, so the blob
// is assembled from multiple encoded parts before the download is started.
let chunks = [];
let totalBytes = 0;

export function reset() {
    chunks = [];
    totalBytes = 0;
}

export function addPart(text) {
    if (!text) return;
    const bytes = new TextEncoder().encode(text);
    chunks.push(bytes);
    totalBytes += bytes.length;
}

export function download(filename) {
    const blob = new Blob(chunks, { type: 'application/x-chess-pgn' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename || 'export.pgn';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(() => URL.revokeObjectURL(url), 30000);
    reset();
}

export function getBytes() {
    return totalBytes;
}
