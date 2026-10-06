import { Cell, Pie, PieChart, ResponsiveContainer, Tooltip } from 'recharts';
import type { MethodSlice } from '../../types';
import { formatNumber } from '../../utils/formatters';
import { Card } from '../shared';

export interface MethodsChartProps {
  data: MethodSlice[];
  totalPacientes: number;
}

export function MethodsChart({ data, totalPacientes }: MethodsChartProps) {
  const nn = data.find((s) => s.key === 'nn')?.value ?? 0;
  const identified = totalPacientes ? 100 - nn : null;
  const description = data.map((s) => `${s.label} ${s.value} %`).join(', ');

  return (
    <Card
      title="Métodos de identificación"
      subtitle={<span className="tabular">{formatNumber(totalPacientes)} pacientes en el rango</span>}
    >
      <div className="flex flex-col gap-4">
        <figure className="relative m-0 mx-auto h-[160px] w-[160px]" aria-label={`Distribución por método: ${description}`}>
          <ResponsiveContainer width="100%" height="100%">
            <PieChart>
              <Pie
                data={data}
                dataKey="value"
                nameKey="label"
                innerRadius={52}
                outerRadius={72}
                startAngle={90}
                endAngle={-270}
                paddingAngle={data.filter((d) => d.value > 0).length > 1 ? 1 : 0}
                stroke="none"
                isAnimationActive={false}
              >
                {data.map((s) => (
                  <Cell key={s.key} fill={s.color} />
                ))}
              </Pie>
              <Tooltip formatter={(v: any) => `${v} %`} contentStyle={{ fontSize: 12, borderRadius: 8, borderColor: '#DCE2E8' }} />
            </PieChart>
          </ResponsiveContainer>
          <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center gap-0.5">
            <span className="tabular text-2xl font-semibold text-ink">{identified === null ? '—' : `${identified} %`}</span>
            <span className="text-[11px] text-ink-muted">identificados</span>
          </div>
        </figure>
        <ul className="m-0 flex list-none flex-col gap-2 p-0">
          {data.map((s) => (
            <li key={s.key} className="flex items-center gap-2.5 text-[13px]">
              <span className="h-2.5 w-2.5 shrink-0 rounded-sm" style={{ background: s.color }} aria-hidden="true" />
              <span className="flex-1 text-ink">{s.label}</span>
              <span className="tabular font-semibold">{s.value} %</span>
            </li>
          ))}
        </ul>
      </div>
    </Card>
  );
}
