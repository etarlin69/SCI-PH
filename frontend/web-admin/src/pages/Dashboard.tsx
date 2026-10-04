import { useState, type ReactNode } from 'react';
import { SlidersHorizontal } from 'lucide-react';
import type { WidgetId } from '../types';
import { CustomizePanel, DashboardGrid, FilterPanel, HospitalChart, MethodsChart, TransferSummaryTable, TrendChart } from '../components/dashboard';
import { Button, ErrorState, LoadingSpinner, Select, useToast } from '../components/shared';
import { DEFAULT_VIEW_NAME, useDashboard } from '../hooks/useDashboard';
import { cn } from '../utils/cn';
import { formatDMY, formatDateTime } from '../utils/dates';

const WIDGET_SPAN: Record<WidgetId, string> = {
    kpis: 'xl:col-span-3',
    trend: 'xl:col-span-2',
    methods: 'xl:col-span-1',
    hospitals: 'xl:col-span-1',
    events: 'xl:col-span-2',
};

export default function Dashboard() {
    const d = useDashboard();
    const [customizing, setCustomizing] = useState(false);
    const toast = useToast();
    const retry = () => void d.refresh();

    const rangeText = d.range.from === d.range.to ? formatDMY(d.range.from) : `${formatDMY(d.range.from)} – ${formatDMY(d.range.to)}`;
    const preset = d.presets.find((p) => p.range.from === d.range.from && p.range.to === d.range.to);

    const viewOptions = [{ value: DEFAULT_VIEW_NAME, label: DEFAULT_VIEW_NAME }];
    if (d.savedView) viewOptions.unshift({ value: d.savedView.name, label: d.savedView.name });

    const renderWidget = (id: WidgetId): ReactNode => {
        if (id === 'kpis') {
            return <DashboardGrid kpis={d.data?.kpis ?? null} range={d.range} today={d.maxDate} metrics={d.metrics} loading={d.loading} />;
        }
        if (!d.data) return null;
        switch (id) {
            case 'trend':
                return <TrendChart data={d.data.trend} range={d.range} change={d.data.trendChange} />;
            case 'methods':
                return <MethodsChart data={d.data.methods} totalPacientes={d.data.kpis.pacientes} />;
            case 'hospitals':
                return <HospitalChart data={d.data.hospitals} subtitle={`${preset?.label ?? 'Rango personalizado'} · ${rangeText}`} />;
            case 'events':
                return <TransferSummaryTable rows={d.data.transfers} loading={d.loading} />;
        }
    };

    return (
        <div className="flex flex-col gap-6">
            <div className="flex flex-wrap items-end justify-between gap-4 lg:gap-6">
                <div className="flex min-w-0 flex-col gap-1.5">
                    <h1 className="m-0 text-2xl font-semibold tracking-tight lg:text-[26px]">Dashboard operativo</h1>
                    <p className="m-0 text-[13px] text-ink-muted">
                        <span className="tabular">Actualizado {d.lastUpdated ? formatDateTime(d.lastUpdated) : '—'}</span> · Actualización
                        automática cada 5 min
                    </p>
                </div>
                <div className="flex w-full flex-wrap items-center gap-2.5 sm:w-auto">
                    <Select
                        label="Vista"
                        layout="inline"
                        options={viewOptions}
                        value={d.activeView}
                        onChange={(e) => d.setActiveView(e.target.value)}
                        className="min-w-[170px] sm:min-w-[220px]"
                    />
                    <Button
                        variant="secondary"
                        onClick={() => setCustomizing(true)}
                        leftIcon={<SlidersHorizontal size={16} aria-hidden="true" />}
                    >
                        Personalizar dashboard
                    </Button>
                </div>
            </div>

            <FilterPanel
                range={d.range}
                onRangeChange={d.setRange}
                presets={d.presets}
                minDate={d.minDate}
                maxDate={d.maxDate}
                hospitals={d.hospitals}
                onHospitalsChange={d.setHospitals}
                metrics={d.metrics}
                onToggleMetric={d.toggleMetric}
                loading={d.loading}
                onRefresh={retry}
            />

            {d.status === 'error' && d.data && (
                <ErrorState
                    variant="inline"
                    title="No se pudo actualizar el dashboard."
                    description={d.lastUpdated ? `Se muestran los datos de ${formatDateTime(d.lastUpdated)}.` : undefined}
                    onRetry={retry}
                />
            )}

            {!d.data ? (
                d.status === 'error' ? (
                    <ErrorState
                        title="No se pudo cargar el dashboard"
                        description="Comprueba tu conexión e inténtalo de nuevo. Si el problema continúa, contacta con soporte."
                        onRetry={retry}
                        className="rounded-[10px] border border-line bg-white"
                    />
                ) : (
                    <div className="flex justify-center py-24">
                        <LoadingSpinner size="lg" label="Cargando dashboard…" />
                    </div>
                )
            ) : (
                <div
                    aria-busy={d.loading || undefined}
                    className={cn(
                        'grid grid-cols-1 gap-6 transition-opacity xl:grid-flow-row-dense xl:grid-cols-3',
                        d.loading && 'opacity-60',
                    )}
                >
                    {d.visibleWidgets.map((w) => (
                        <div key={w.id} className={cn('min-w-0', WIDGET_SPAN[w.id])}>
                            {renderWidget(w.id)}
                        </div>
                    ))}
                </div>
            )}

            <CustomizePanel
                open={customizing}
                onClose={() => setCustomizing(false)}
                widgets={d.widgets}
                viewName={d.activeView}
                onSave={(view) => {
                    d.saveView(view);
                    toast({ title: 'Vista guardada', description: `«${view.name}» es ahora tu vista activa.` });
                }}
            />
        </div>
    );
}
