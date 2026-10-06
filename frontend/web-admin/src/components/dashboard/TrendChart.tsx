import {
  Area,
  CartesianGrid,
  ComposedChart,
  Line,
  ReferenceArea,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import type { DateRange, TrendPoint } from '../../types';
import { formatDM, formatDMY } from '../../utils/dates';
import { formatSignedPercent } from '../../utils/formatters';
import { Card } from '../shared';

export interface TrendChartProps {
  data: TrendPoint[];
  range: DateRange;
  change: number;
  height?: number;
}

const COLORS = { completados: '#1B4F8A', activos: '#E07B1F', band: '#E1EAF4', grid: '#EDF0F3', axis: '#C5CED8', tick: '#5B6878' };

function TrendTooltip({ active, payload, label }: any) {
  if (!active || !payload?.length) return null;
  return (
    <div className="rounded-lg border border-line bg-white px-3 py-2 text-xs shadow-md">
      <p className="mb-1 font-semibold text-ink">{formatDMY(String(label))}</p>
      {payload.map((p: any) => (
        <p key={String(p.dataKey)} className="flex items-center gap-2 text-ink-3">
          <span className="h-2 w-2 rounded-full" style={{ background: p.color }} aria-hidden="true" />
          {p.name}: <span className="tabular font-semibold text-ink">{p.value}</span>
        </p>
      ))}
    </div>
  );
}

export function TrendChart({ data, range, change, height = 280 }: TrendChartProps) {
  const windowStart = data[0]?.date ?? range.from;
  const windowEnd = data[data.length - 1]?.date ?? range.to;
  const bandFrom = range.from < windowStart ? windowStart : range.from;
  const singleDay = bandFrom === range.to;
  const last = data[data.length - 1];

  return (
    <Card
      title="Episodios por día"
      subtitle={
        <span className="tabular">
          30 días hasta {formatDMY(windowEnd)} · completados{' '}
          <strong className={change >= 0 ? 'text-success-600' : 'text-warning-700'}>{formatSignedPercent(change)}</strong> vs 7 días previos
        </span>
      }
      actions={
        <div className="flex flex-wrap gap-x-4 gap-y-1 text-[13px] text-ink-3" aria-hidden="true">
          <span className="flex items-center gap-1.5">
            <span className="h-3 w-3 rounded-sm bg-chart-band" />
            Rango
          </span>
          <span className="flex items-center gap-1.5">
            <span className="h-[3px] w-3.5 rounded bg-chart-blue" />
            Completados
          </span>
          <span className="flex items-center gap-1.5">
            <span className="h-[3px] w-3.5 rounded bg-chart-orange" />
            Activos
          </span>
        </div>
      }
    >
      <figure className="m-0">
        <div style={{ height }}>
          <ResponsiveContainer width="100%" height="100%">
            <ComposedChart data={data} margin={{ top: 8, right: 12, bottom: 0, left: -12 }}>
              <CartesianGrid vertical={false} stroke={COLORS.grid} />
              {singleDay ? (
                <ReferenceLine x={range.to} stroke={COLORS.band} strokeWidth={14} ifOverflow="hidden" />
              ) : (
                <ReferenceArea x1={bandFrom} x2={range.to} fill={COLORS.band} fillOpacity={0.7} ifOverflow="hidden" />
              )}
              <XAxis
                dataKey="date"
                tickFormatter={(d: string) => formatDM(d)}
                interval={5}
                tick={{ fontSize: 11, fill: COLORS.tick }}
                axisLine={{ stroke: COLORS.axis }}
                tickLine={false}
              />
              <YAxis allowDecimals={false} tick={{ fontSize: 11, fill: COLORS.tick }} axisLine={false} tickLine={false} width={44} />
              <Tooltip content={<TrendTooltip />} cursor={{ stroke: COLORS.axis }} />
              <Area
                type="monotone"
                dataKey="completados"
                fill={COLORS.completados}
                fillOpacity={0.08}
                stroke="none"
                tooltipType="none"
                isAnimationActive={false}
              />
              <Line
                type="monotone"
                dataKey="completados"
                name="Completados"
                stroke={COLORS.completados}
                strokeWidth={2.5}
                dot={false}
                activeDot={{ r: 4 }}
                isAnimationActive={false}
              />
              <Line
                type="monotone"
                dataKey="activos"
                name="Activos"
                stroke={COLORS.activos}
                strokeWidth={2.5}
                dot={false}
                activeDot={{ r: 4 }}
                isAnimationActive={false}
              />
            </ComposedChart>
          </ResponsiveContainer>
        </div>
        {last && (
          <figcaption className="sr-only">
            Del {formatDMY(windowStart)} al {formatDMY(windowEnd)}. Último día: {last.completados} completados y {last.activos} activos.
            Variación de completados {formatSignedPercent(change)} respecto a los 7 días previos.
          </figcaption>
        )}
      </figure>
    </Card>
  );
}
