import type { HospitalId, ISODate } from '../types';
import { addDays } from '../utils/dates';
import { HOSPITAL_IDS } from './index';

export interface DailyHospitalStats {
    episodios: number;
    activos: number;
    transferencias: number;
    fallidas: number;
    pacientes: number;
    nn: number;

    tiempo: number;
}

export interface DailySeries {
    startDate: ISODate;
    endDate: ISODate;
    days: number;
    byHospital: Record<HospitalId, DailyHospitalStats[]>;
}

export const SERIES_DAYS = 149;

const COMPLETADOS_30 = [
    31, 29, 34, 33, 30, 36, 35, 32, 34, 37, 36, 33, 38, 39, 35, 37, 40, 37, 36, 40, 42, 39, 39, 44, 43, 41, 46, 48, 45, 47,
];
const ACTIVOS_30 = [9, 11, 8, 10, 12, 9, 10, 13, 11, 9, 12, 10, 11, 13, 12, 10, 14, 11, 12, 13, 11, 12, 14, 12, 13, 15, 12, 14, 13, 12];

const WEIGHTS = [0.31, 0.24, 0.19, 0.15, 0.11];
const TIME_OFFSET = [-0.6, 0.4, -0.2, 0.9, 0.3];

function noise(a: number, b: number): number {
    const x = Math.sin(a * 12.9898 + b * 78.233) * 43758.5453;
    return x - Math.floor(x);
}

function allocate(total: number, weights: number[]): number[] {
    const sum = weights.reduce((a, b) => a + b, 0) || 1;
    const raw = weights.map((w) => (total * w) / sum);
    const out = raw.map(Math.floor);
    const rest = total - out.reduce((a, b) => a + b, 0);
    const order = raw.map((x, i) => ({ frac: x - Math.floor(x), i })).sort((a, b) => b.frac - a.frac);
    for (let k = 0; k < rest; k++) out[order[k % order.length].i]++;
    return out;
}

export function buildDailySeries(endDate: ISODate): DailySeries {
    const today = SERIES_DAYS - 1;
    const byHospital = Object.fromEntries(HOSPITAL_IDS.map((id) => [id, [] as DailyHospitalStats[]])) as Record<
        HospitalId,
        DailyHospitalStats[]
    >;

    for (let i = 0; i < SERIES_DAYS; i++) {
        const fromLast30 = i - (SERIES_DAYS - 30);
        const completados =
            fromLast30 >= 0 ? COMPLETADOS_30[fromLast30] : Math.max(18, Math.round(28 + 4 * Math.sin(i / 5) + noise(i, 1) * 6 + i * 0.03));
        const activos = fromLast30 >= 0 ? ACTIVOS_30[fromLast30] : Math.round(8 + noise(i, 2) * 6);

        const shares = i === today ? WEIGHTS : WEIGHTS.map((w, h) => w * (0.8 + 0.4 * noise(i, h + 10)));
        const ep = allocate(completados, shares);
        const act = allocate(activos, WEIGHTS);
        const tr = allocate(Math.round(completados * 0.15), ep);
        const pacTotal = Math.round(completados * 0.85);
        const pac = allocate(pacTotal, ep);
        const nnTotal = i === today ? 2 : Math.max(0, Math.round(pacTotal * 0.055 + (noise(i, 7) - 0.5) * 1.6));
        const nn = allocate(nnTotal, ep);
        const failHospital = Math.floor(noise(i, 6) * HOSPITAL_IDS.length);
        const hasFailure = i !== today && noise(i, 5) < 0.15;
        const baseTime = i === today ? 8.5 : 8.2 + noise(i, 3) * 2.4 + (i >= SERIES_DAYS - 26 ? 0.3 : 0);

        HOSPITAL_IDS.forEach((id, h) => {
            byHospital[id].push({
                episodios: ep[h],
                activos: act[h],
                transferencias: tr[h],
                fallidas: hasFailure && h === failHospital && tr[h] > 0 ? 1 : 0,
                pacientes: pac[h],
                nn: Math.min(nn[h], pac[h]),
                tiempo: baseTime + TIME_OFFSET[h],
            });
        });
    }

    return { startDate: addDays(endDate, -(SERIES_DAYS - 1)), endDate, days: SERIES_DAYS, byHospital };
}
