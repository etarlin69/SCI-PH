import { Bar, BarChart, Cell, LabelList, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import type { HospitalCount } from '../../types';
import { formatNumber } from '../../utils/formatters';
import { Card } from '../shared';

export interface HospitalChartProps {
    data: HospitalCount[];
    subtitle: string;
}

export function HospitalChart({ data, subtitle }: HospitalChartProps) {
    const rows = data.map((d) => ({ nombre: d.hospital.nombre, episodios: d.episodios, color: d.hospital.color }));
    const height = Math.max(120, rows.length * 48 + 8);

    return (
        <Card title="Episodios por hospital" subtitle={subtitle}>
            <figure className="m-0" aria-label={rows.map((r) => `${r.nombre}: ${r.episodios}`).join(', ')}>
                <div style={{ height }}>
                    <ResponsiveContainer width="100%" height="100%">
                        <BarChart data={rows} layout="vertical" margin={{ top: 0, right: 40, bottom: 0, left: 0 }} barCategoryGap={14}>
                            <XAxis type="number" hide domain={[0, 'dataMax']} />
                            <YAxis
                                type="category"
                                dataKey="nombre"
                                width={140}
                                tick={{ fontSize: 13, fill: '#0F1B2A' }}
                                axisLine={false}
                                tickLine={false}
                            />
                            <Tooltip
                                cursor={{ fill: '#F7F9FB' }}
                                formatter={(v: any) => [formatNumber(v), 'Episodios']}
                                contentStyle={{ fontSize: 12, borderRadius: 8, borderColor: '#DCE2E8' }}
                            />
                            <Bar
                                dataKey="episodios"
                                radius={[3, 3, 3, 3]}
                                background={{ fill: '#EDF0F3', radius: 3 }}
                                isAnimationActive={false}
                            >
                                {rows.map((r) => (
                                    <Cell key={r.nombre} fill={r.color} />
                                ))}
                                <LabelList
                                    dataKey="episodios"
                                    position="right"
                                    formatter={(v: any) => formatNumber(v)}
                                    style={{ fontSize: 13, fontWeight: 600, fill: '#0F1B2A' }}
                                />
                            </Bar>
                        </BarChart>
                    </ResponsiveContainer>
                </div>
            </figure>
        </Card>
    );
}
