const numberFormat = new Intl.NumberFormat('en-US');

/** 1012 → "1,012" */
export function formatNumber(n: number): string {
  return numberFormat.format(n);
}

/** 97.64 → "97.6 %", 100 → "100 %", null → "—" */
export function formatPercent(value: number | null, decimals = 0): string {
  if (value === null || Number.isNaN(value)) return '—';
  return `${Number(value.toFixed(decimals))} %`;
}

/** 8.54 → "8.5" */
export function formatMinutes(value: number | null): string {
  return value === null ? '—' : value.toFixed(1);
}

/** +15 → "+15 %", -3 → "−3 %" */
export function formatSignedPercent(value: number): string {
  if (value > 0) return `+${value} %`;
  if (value < 0) return `−${Math.abs(value)} %`;
  return '0 %';
}

/** "Dra. */
export function initials(name: string): string {
  return name
    .replace(/^(Dr\.|Dra\.)\s*/i, '')
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase() ?? '')
    .join('');
}
