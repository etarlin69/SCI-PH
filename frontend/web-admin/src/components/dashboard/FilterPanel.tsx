import { Check, RefreshCw } from 'lucide-react';
import type { DateRange, HospitalId, ISODate, MetricType } from '../../types';
import { HOSPITALES } from '../../data';
import { cn } from '../../utils/cn';
import { diffDays } from '../../utils/dates';
import { Button, DateRangePicker, MultiSelect, type DateRangePreset } from '../shared';

export interface FilterPanelProps {
    range: DateRange;
    onRangeChange: (_: DateRange) => void;
    presets: DateRangePreset[];
    minDate: ISODate;
    maxDate: ISODate;
    hospitals: HospitalId[];
    onHospitalsChange: (_ids: HospitalId[]) => void;
    metrics: MetricType[];
    onToggleMetric: (_metric: MetricType) => void;
    loading: boolean;
    onRefresh: () => void;
}

const METRIC_LABEL: Record<MetricType, string> = {
    episodios: 'Episodios',
    transferencias: 'Transferencias',
    identificaciones: 'Identificaciones',
};

export function FilterPanel({
    range: _range,
    onRangeChange,
    presets,
    minDate,
    maxDate,
    hospitals,
    onHospitalsChange,
    metrics: _metrics,
    onToggleMetric,
    loading,
    onRefresh,
}: FilterPanelProps) {
    const days = diffDays(_range.from, _range.to) + 1;
    const preset = presets.find((p) => p.range.from === _range.from && p.range.to === _range.to);
    const scope = [
        preset?.label ?? 'Rango personalizado',
        `${days} ${days === 1 ? 'día' : 'días'}`,
        hospitals.length === HOSPITALES.length
            ? 'todos los hospitales'
            : `${hospitals.length} ${hospitals.length === 1 ? 'hospital' : 'hospitales'}`,
    ].join(' · ');

    return (
        <section
            aria-label="Filtros del dashboard"
            className="relative z-10 flex flex-col gap-3.5 rounded-[10px] border border-line bg-white px-4 pb-4 pt-3.5 sm:px-5"
        >
            <div className="flex flex-wrap items-end gap-4 sm:gap-6">
                <DateRangePicker
                    value={_range}
                    onChange={onRangeChange}
                    min={minDate}
                    max={maxDate}
                    presets={presets}
                    footnote={`Datos desde ${minDate.split('-').reverse().join('/')}`}
                />
                <MultiSelect
                    label="Hospital"
                    allLabel="Todos los hospitales"
                    options={HOSPITALES.map((h) => ({ value: h.id, label: h.nombre, color: h.color }))}
                    value={hospitals}
                    onChange={onHospitalsChange}
                />
            </div>

            <div className="flex flex-wrap items-end gap-4 sm:gap-6">
                <div className="flex flex-col gap-1.5">
                    <span className="text-xs font-semibold text-ink-3">Tipo de métrica</span>
                    <div role="group" aria-label="Tipo de métrica" className="flex flex-wrap gap-1.5">
                        {(Object.keys(METRIC_LABEL) as MetricType[]).map((m) => {
                            const on = _metrics.includes(m);
                            return (
                                <button
                                    key={m}
                                    type="button"
                                    aria-pressed={on}
                                    onClick={() => onToggleMetric(m)}
                                    className={cn(
                                        'flex h-9 items-center gap-1.5 rounded-md border px-3 text-[13px]',
                                        on
                                            ? 'border-primary-300 bg-primary-100 text-primary-700'
                                            : 'border-line-strong bg-white text-ink-3 hover:bg-surface-hover',
                                    )}
                                >
                                    <Check size={14} className={on ? 'opacity-100' : 'opacity-0'} aria-hidden="true" />
                                    {METRIC_LABEL[m]}
                                </button>
                            );
                        })}
                    </div>
                </div>
                <span role="status" className="tabular ml-auto self-center text-xs text-ink-muted">
                    {scope}
                </span>
                <Button
                    size="sm"
                    onClick={onRefresh}
                    loading={loading}
                    loadingText="Actualizando…"
                    leftIcon={<RefreshCw size={16} aria-hidden="true" />}
                >
                    Actualizar dashboard
                </Button>
            </div>
        </section>
    );
}
