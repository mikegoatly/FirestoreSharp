// ── Value rendering ────────────────────────────────────────────────────────

import { esc } from '/ui/api.js';

export function renderValue(uiVal, depth = 0) {
  if (!uiVal) return '<span class="val val-null">null</span>';
  const { type, value } = uiVal;
  switch (type) {
    case 'null':
      return '<span class="val val-null">null</span>';
    case 'bool':
      return `<span class="val val-bool">${esc(value)}</span>`;
    case 'int':
      return `<span class="val val-int">${esc(value)}</span>`;
    case 'double':
      return `<span class="val val-double">${esc(value)}</span>`;
    case 'string':
      return `<span class="val val-string">"${esc(value)}"</span>`;
    case 'timestamp':
      return `<span class="val val-timestamp">${esc(value)}</span>`;
    case 'bytes':
      return `<span class="val val-bytes">bytes(${esc(value).slice(0, 20)}…)</span>`;
    case 'reference':
      return `<span class="val val-reference">${esc(value)}</span>`;
    case 'geopoint': {
      const g = value || {};
      return `<span class="val val-geopoint">LatLng(${esc(g.latitude ?? '?')}, ${esc(g.longitude ?? '?')})</span>`;
    }
    case 'array': {
      const items = Array.isArray(value) ? value : [];
      if (depth > 1 || items.length === 0)
        return `<span class="val val-array">[${items.length} items]</span>`;
      const inner = items.slice(0, 5).map(v => renderValue(v, depth + 1)).join(', ');
      const more = items.length > 5 ? `, …+${items.length - 5}` : '';
      return `<span class="val val-array">[${inner}${more}]</span>`;
    }
    case 'map': {
      const keys = value ? Object.keys(value) : [];
      if (depth > 1 || keys.length === 0)
        return `<span class="val val-map">{${keys.length} keys}</span>`;
      const inner = keys.slice(0, 3).map(k =>
        `<span class="field-key">${esc(k)}</span> ${renderValue(value[k], depth + 1)}`
      ).join(', ');
      const more = keys.length > 3 ? `, …+${keys.length - 3}` : '';
      return `<span class="val val-map">{${inner}${more}}</span>`;
    }
    default:
      return `<span class="val">${esc(JSON.stringify(value))}</span>`;
  }
}
