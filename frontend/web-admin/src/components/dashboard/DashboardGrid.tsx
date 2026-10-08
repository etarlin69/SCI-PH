import type { DashboardKpis, DateRange, MetricType } from '../../types';
import { diffDays, formatDM } from '../../utils/dates';
import { formatMinutes, formatNumber, formatPercent } from '../../utils/formatters';
import { KPICard, type KPICardProps, type KpiStatus } from './KPICard';

export interface DashboardGridProps {
  kpis: DashboardKpis | null;
  range: DateRange;
  today: string;
  metrics: MetricType[];
  loading?: boolean;
}

export const RESPONSE_TIME_TARGET = { ok: 10, warn: 15 };

function responseStatus(minutes: number | null): { level: KpiStatus; text: string } | undefined {
  if (minutes === null) return undefined;
  if (minutes <= RESPONSE_TIME_TARGET.ok) return { level: 'ok', text: `Dentro de meta (≤ ${RESPONSE_TIME_TARGET.ok} min)` };
  if (minutes <= RESPONSE_TIME_TARGET.warn)
    return { level: 'warn', text: `Cerca del límite (${RESPONSE_TIME_TARGET.ok}–${RESPONSE_TIME_TARGET.warn} min)` };
  return { level: 'crit', text: `Fuera de meta (> ${RESPONSE_TIME_TARGET.warn} min)` };
}

export function DashboardGrid({ kpis, range, today, metrics, loading = false }: DashboardGridProps) {
  const days = diffDays(range.from, range.to) + 1;
  const isToday = range.to === today;

  const transferLabel =
    days === 1 ? (isToday ? 'Transferencias hoy' : `Transferencias ${formatDM(range.to)}`) : `Transferencias (${days} días)`;
  const delta = kpis ? kpis.episodiosActivos - kpis.episodiosActivosDiaAnterior : 0;

  const cards: { key: string; metric: MetricType; props: KPICardProps }[] = [
    {
      key: 'activos',
      metric: 'episodios',
      props: {
        label: isToday ? 'Episodios activos' : `Episodios activos al ${formatDM(range.to)}`,
        value: kpis ? formatNumber(kpis.episodiosActivos) : '—',
        delta: kpis
          ? {
              value: `${delta > 0 ? '+' : ''}${delta}`,
              direction: delta > 0 ? 'up' : delta < 0 ? 'down' : 'flat',
              tone: delta > 0 ? 'positive' : delta < 0 ? 'negative' : 'neutral',
              caption: `vs día anterior (${kpis.episodiosActivosDiaAnterior})`,
            }
          : undefined,
      },
    },
    {
      key: 'transferencias',
      metric: 'transferencias',
      props: {
        label: transferLabel,
        value: kpis ? formatNumber(kpis.transferencias) : '—',
        highlight: kpis ? { value: formatPercent(kpis.tasaExito, 1), caption: 'de éxito' } : undefined,
      },
    },
    {
      key: 'identificados',
      metric: 'identificaciones',
      props: {
        label: 'Pacientes identificados',
        value: kpis ? formatPercent(kpis.pctIdentificados) : '—',
        caption: kpis
          ? `${formatNumber(kpis.pacientes - kpis.pacientesNN)} de ${formatNumber(kpis.pacientes)} pacientes · ${formatNumber(kpis.pacientesNN)} NN`
          : undefined,
      },
    },
    {
      key: 'tiempo',
      metric: 'episodios',
      props: {
        label: 'Tiempo promedio de respuesta',
        value: kpis ? formatMinutes(kpis.tiempoPromedioRespuesta) : '—',
        unit: 'min',
        status: kpis ? responseStatus(kpis.tiempoPromedioRespuesta) : undefined,
      },
    },
  ];

  const visible = cards.filter((c) => metrics.includes(c.metric));
  if (visible.length === 0) return null;

  return (
    <section aria-label="Indicadores clave" className="grid grid-cols-1 gap-6 sm:grid-cols-2 xl:grid-cols-4">
      {visible.map((c) => (
        <KPICard key={c.key} {...c.props} loading={loading && !kpis} />
      ))}
    </section>
  );
}
