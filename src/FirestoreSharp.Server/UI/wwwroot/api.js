// ── API & utilities ────────────────────────────────────────────────────────

export const API = '/api/ui';

export async function apiFetch(url, options = {}) {
  const res = await fetch(url, options);
  if (!res.ok) {
    let msg = `HTTP ${res.status}`;
    try { const j = await res.json(); msg = j.detail || j.title || msg; } catch {}
    throw new Error(msg);
  }
  if (res.status === 204) return null;
  return res.json();
}

// Escapes a value for safe interpolation into HTML content or double-quoted attributes.
// Not safe for single-quoted attributes, unquoted attributes, or JS/URL contexts.
export function esc(s) {
  return String(s)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}
