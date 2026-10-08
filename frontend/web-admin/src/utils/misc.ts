export function delay(ms = 500): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/** Siguiente ID con prefijo y relleno: `nextId('MED', ['MED001','MED012'], 3)` → `'MED013'`. */
export function nextId(prefix: string, existing: string[], width = 3): string {
  const max = existing.reduce((m, id) => {
    const n = Number(id.slice(prefix.length));
    return id.startsWith(prefix) && Number.isFinite(n) ? Math.max(m, n) : m;
  }, 0);
  return `${prefix}${String(max + 1).padStart(width, '0')}`;
}

const pad = (n: number) => String(n).padStart(2, '0');

/** `YYYY-MM-DD HH:mm:ss` en hora local. */
export function nowTimestamp(date = new Date()): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
}

/** `YYYY-MM-DD HH:mm` en hora local. */
export function nowMinutes(date = new Date()): string {
  return nowTimestamp(date).slice(0, 16);
}

/** Navegador y sistema a partir del User-Agent (para los metadatos de auditoría). */
export function detectClient(ua = typeof navigator === 'undefined' ? '' : navigator.userAgent): { navegador: string; sistema: string } {
  // El orden importa: Edge incluye "Chrome" y Chrome incluye "Safari" en su User-Agent.
  const patterns: [RegExp, string][] = [
    [/Edg\/(\d+)/, 'Edge'],
    [/Firefox\/(\d+)/, 'Firefox'],
    [/Chrome\/(\d+)/, 'Chrome'],
    [/Version\/(\d+).*Safari/, 'Safari'],
  ];
  const hit = patterns.map(([re, name]) => [ua.match(re), name] as const).find(([m]) => m);
  const browser = hit ? `${hit[1]} ${hit[0]?.[1] ?? ''}`.trim() : 'Desconocido';
  const os = /Windows NT 10/.test(ua)
    ? 'Windows 10/11'
    : /Mac OS X/.test(ua)
      ? 'macOS'
      : /Android/.test(ua)
        ? 'Android'
        : /Linux/.test(ua)
          ? 'Linux'
          : 'Desconocido';
  return { navegador: browser, sistema: os };
}
