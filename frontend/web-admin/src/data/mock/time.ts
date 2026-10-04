const pad = (n: number) => String(n).padStart(2, '0');

export const MOCK_NOW = new Date();

export interface Offset {
  days?: number;
  hours?: number;
  minutes?: number;
}

function shifted({ days = 0, hours = 0, minutes = 0 }: Offset, base = MOCK_NOW): Date {
  return new Date(base.getTime() - ((days * 24 + hours) * 60 + minutes) * 60_000);
}

/** `YYYY-MM-DD HH:mm` hace X tiempo. */
export function ago(offset: Offset): string {
  const d = shifted(offset);
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** `YYYY-MM-DD` hace X días (negativo = en el futuro). */
export function daysAgo(days: number): string {
  return ago({ days }).slice(0, 10);
}

/** `YYYY-MM-DD` dentro de X días. */
export function inDays(days: number): string {
  return daysAgo(-days);
}

/** `YYYY-MM-DD HH:mm` del día `days` atrás a la hora indicada (`'09:14'`). */
export function at(days: number, time: string): string {
  return `${daysAgo(days)} ${time}`;
}

/** Generador pseudoaleatorio determinista (mismos datos en cada recarga). */
export function seededRandom(seed: number): () => number {
  let h = seed >>> 0;
  return () => {
    h = (Math.imul(h, 1103515245) + 12345) >>> 0;
    return (h >>> 8) / 16_777_216;
  };
}
