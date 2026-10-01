export function delay(ms = 500): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms));
}

export function nextId(prefix: string, existing: string[], width = 3): string {
    const max = existing.reduce((m, id) => {
        const n = Number(id.slice(prefix.length));
        return id.startsWith(prefix) && Number.isFinite(n) ? Math.max(m, n) : m;
    }, 0);
    return `${prefix}${String(max + 1).padStart(width, '0')}`;
}

const pad = (n: number) => String(n).padStart(2, '0');

export function nowTimestamp(date = new Date()): string {
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
}

export function nowMinutes(date = new Date()): string {
    return nowTimestamp(date).slice(0, 16);
}

export function detectClient(ua = typeof navigator === 'undefined' ? '' : navigator.userAgent): { navegador: string; sistema: string } {
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
