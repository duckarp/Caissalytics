const STORAGE_KEY = 'caissalytics_workspace_state';
const BACKUP_KEY = 'caissalytics_workspace_state_backup';

// Returns false when the state could not be stored (e.g. localStorage quota exceeded)
export function saveWorkspaceState(json) {
    try {
        if (json) {
            localStorage.setItem(STORAGE_KEY, json);
        } else {
            localStorage.removeItem(STORAGE_KEY);
        }
        return true;
    } catch (e) {
        console.warn('Unable to save workspace state to localStorage:', e);
        return false;
    }
}

export function loadWorkspaceState() {
    try {
        return localStorage.getItem(STORAGE_KEY);
    } catch (e) {
        console.warn('Unable to load workspace state from localStorage:', e);
        return null;
    }
}

// Copies the stored workspace aside before it can be overwritten (e.g. after a failed restore).
// Returns true when there was nothing to back up or the copy succeeded.
export function backupWorkspaceState() {
    try {
        const current = localStorage.getItem(STORAGE_KEY);
        if (current) {
            localStorage.setItem(BACKUP_KEY, current);
        }
        return true;
    } catch (e) {
        console.warn('Unable to back up workspace state in localStorage:', e);
        return false;
    }
}

export function clearWorkspaceState() {
    try {
        localStorage.removeItem(STORAGE_KEY);
    } catch (e) {
        console.warn('Unable to clear workspace state from localStorage:', e);
    }
}
