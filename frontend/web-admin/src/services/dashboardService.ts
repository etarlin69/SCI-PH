import type {
    DashboardData,
    DateRange,
    HospitalId,
    ISODate,
    MethodSlice,
    MetodoIdentificacion,
    RecentTransfer,
    TrendPoint,
} from '../types';
import { HOSPITALES, TRANSFERENCIAS_MOCK } from '../data';
import { buildDailySeries, type DailyHospitalStats } from '../data/dailySeries';
import { addDays, diffDays, isWithin, todayISO } from '../utils/dates';

export interface DashboardFilters {
    range: DateRange;
    hospitals: HospitalId[];
}

export const TREND_DAYS = 30;

const series = buildDailySeries(todayISO());

export const DASHBOARD_MIN_DATE: ISODate = addDays(series.startDate, TREND_DAYS - 1);
export const DASHBOARD_MAX_DATE: ISODate = series.endDate;

const METHOD_META: { key: Exclude<MetodoIdentificacion, 'nn'>; label: string; color: string; share: number }[] = [
    { key: 'ocr', label: 'OCR (cédula)', color: '#1B4F8A', share: 0.61 },
    { key: 'qr', label: 'QR SCI-PH', color: '#1C7C7D', share: 0.23 },
    { key: 'huella', label: 'Huella digital', color: '#7FA9D9', share: 0.095 },
    { key: 'manual', label: 'Manual', color: '#6B4FA0', share: 0.065 },
];

const indexOf = (date: ISODate) => diffDays(series.startDate, date);

function sumDay(hospitals: HospitalId[], i: number, pick: (d: DailyHospitalStats) => number): number {
    return hospitals.reduce((t, id) => t + (series.byHospital[id][i] ? pick(series.byHospital[id][i]) : 0), 0);
}

function sumRange(hospitals: HospitalId[], from: number, to: number, pick: (d: DailyHospitalStats) => number): number {
    let total = 0;
    for (let i = from; i <= to; i++) total += sumDay(hospitals, i, pick);
    return total;
}

function buildMethods(pacientes: number, nn: number): MethodSlice[] {
    const nnShare = pacientes ? nn / pacientes : 0;
    const slices: MethodSlice[] = METHOD_META.map((m) => ({
        key: m.key,
        label: m.label,
        color: m.color,
        value: Math.round(m.share * (1 - nnShare) * 100),
    }));
    slices.push({ key: 'nn', label: 'No identificado (NN)', color: '#E07B1F', value: Math.round(nnShare * 100) });
    const diff = 100 - slices.reduce((t, s) => t + s.value, 0);
    slices[0].value += diff;
    return slices;
}

export function computeDashboard({ range, hospitals }: DashboardFilters): DashboardData {
    const from = Math.max(0, indexOf(range.from));
    const to = Math.min(series.days - 1, indexOf(range.to));

    const transferencias = sumRange(hospitals, from, to, (d) => d.transferencias);
    const fallidas = sumRange(hospitals, from, to, (d) => d.fallidas);
    const pacientes = sumRange(hospitals, from, to, (d) => d.pacientes);
    const nn = sumRange(hospitals, from, to, (d) => d.nn);
    let tiempoPonderado = 0;
    let episodiosRango = 0;
    for (let i = from; i <= to; i++) {
        for (const id of hospitals) {
            const d = series.byHospital[id][i];
            tiempoPonderado += d.episodios * d.tiempo;
            episodiosRango += d.episodios;
        }
    }

    const trend: TrendPoint[] = [];
    for (let k = TREND_DAYS - 1; k >= 0; k--) {
        const i = to - k;
        trend.push({
            date: addDays(range.to, -k),
            completados: i >= 0 ? sumDay(hospitals, i, (d) => d.episodios) : 0,
            activos: i >= 0 ? sumDay(hospitals, i, (d) => d.activos) : 0,
        });
    }
    const last7 = trend.slice(-7).reduce((t, p) => t + p.completados, 0);
    const prev7 = trend.slice(-14, -7).reduce((t, p) => t + p.completados, 0);

    const selectedNames = new Set(HOSPITALES.filter((h) => hospitals.includes(h.id)).map((h) => h.nombre));
    const events: RecentTransfer[] = TRANSFERENCIAS_MOCK.map((t) => ({
        id: t.id,
        paciente: t.paciente,
        origen: t.origen,
        destino: t.destino,
        fecha: addDays(series.endDate, -t.diasAtras),
        hora: t.hora,
        estado: t.estado,
    }))
        .filter((t) => isWithin(t.fecha, range.from, range.to) && (selectedNames.has(t.origen) || selectedNames.has(t.destino)))
        .sort((a, b) => (b.fecha + b.hora).localeCompare(a.fecha + a.hora))
        .slice(0, 10);

    return {
        kpis: {
            episodiosActivos: sumDay(hospitals, to, (d) => d.activos),
            episodiosActivosDiaAnterior: to > 0 ? sumDay(hospitals, to - 1, (d) => d.activos) : 0,
            transferencias,
            transferenciasFallidas: fallidas,
            tasaExito: transferencias ? ((transferencias - fallidas) / transferencias) * 100 : null,
            pacientes,
            pacientesNN: nn,
            pctIdentificados: pacientes ? Math.round(((pacientes - nn) / pacientes) * 100) : null,
            tiempoPromedioRespuesta: episodiosRango ? tiempoPonderado / episodiosRango : null,
        },
        trend,
        trendChange: prev7 ? Math.round((last7 / prev7 - 1) * 100) : 0,
        methods: buildMethods(pacientes, nn),
        hospitals: HOSPITALES.filter((h) => hospitals.includes(h.id)).map((hospital) => ({
            hospital,
            episodios: sumRange([hospital.id], from, to, (d) => d.episodios),
        })),
        events,
    };
}

export async function fetchDashboard(filters: DashboardFilters, latencyMs = 450): Promise<DashboardData> {
    await new Promise((resolve) => setTimeout(resolve, latencyMs));
    return computeDashboard(filters);
}
