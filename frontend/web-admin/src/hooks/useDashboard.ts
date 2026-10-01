import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { DashboardData, DateRange, HospitalId, MetricType, SavedView, WidgetConfig, WidgetId } from '../types';
import { HOSPITAL_IDS } from '../data';
import { DASHBOARD_MAX_DATE, DASHBOARD_MIN_DATE, fetchDashboard } from '../services/dashboardService';
import { addDays, startOfMonth } from '../utils/dates';
import type { DateRangePreset } from '../components/shared';

export const DEFAULT_WIDGETS: WidgetConfig[] = [
    { id: 'kpis', label: 'Indicadores clave', visible: true },
    { id: 'trend', label: 'Tendencia de episodios', visible: true },
    { id: 'methods', label: 'Métodos de identificación', visible: true },
    { id: 'hospitals', label: 'Episodios por hospital', visible: true },
    { id: 'events', label: 'Eventos recientes', visible: true },
];

export const DEFAULT_VIEW_NAME = 'Predeterminada';
export const ALL_METRICS: MetricType[] = ['episodios', 'transferencias', 'identificaciones'];

export const WIDGET_METRIC: Record<Exclude<WidgetId, 'kpis'>, MetricType> = {
    trend: 'episodios',
    hospitals: 'episodios',
    events: 'transferencias',
    methods: 'identificaciones',
};

const STORAGE_KEY = 'sciph.dashboard.savedView.v1';
const AUTO_REFRESH_MS = 5 * 60 * 1000;

function loadSavedView(): SavedView | null {
    try {
        const raw = localStorage.getItem(STORAGE_KEY);
        if (!raw) return null;
        const parsed = JSON.parse(raw) as SavedView;

        const known = parsed.widgets.filter((w) => DEFAULT_WIDGETS.some((d) => d.id === w.id));
        const missing = DEFAULT_WIDGETS.filter((d) => !known.some((w) => w.id === d.id));
        return { name: parsed.name, widgets: [...known, ...missing] };
    } catch {
        return null;
    }
}

export function useDashboard() {
    const today = DASHBOARD_MAX_DATE;

    const presets: DateRangePreset[] = useMemo(
        () => [
            { label: 'Hoy', range: { from: today, to: today } },
            { label: 'Esta semana', range: { from: addDays(today, -6), to: today } },
            { label: 'Este mes', range: { from: startOfMonth(today), to: today } },
        ],
        [today],
    );

    const [range, setRange] = useState<DateRange>({ from: today, to: today });
    const [hospitals, setHospitals] = useState<HospitalId[]>(HOSPITAL_IDS);
    const [metrics, setMetrics] = useState<MetricType[]>(ALL_METRICS);
    const [data, setData] = useState<DashboardData | null>(null);
    const [loading, setLoading] = useState(true);
    const [lastUpdated, setLastUpdated] = useState<Date | null>(null);

    const [savedView, setSavedView] = useState<SavedView | null>(loadSavedView);
    const [activeView, setActiveView] = useState<string>(() => savedView?.name ?? DEFAULT_VIEW_NAME);
    const widgets = activeView === savedView?.name ? savedView.widgets : DEFAULT_WIDGETS;

    const requestId = useRef(0);

    const load = useCallback(async () => {
        const id = ++requestId.current;
        setLoading(true);
        try {
            const next = await fetchDashboard({ range, hospitals });
            if (id === requestId.current) {
                setData(next);
                setLastUpdated(new Date());
            }
        } catch {
        } finally {
            if (id === requestId.current) setLoading(false);
        }
    }, [range, hospitals]);

    useEffect(() => {
        void load();
    }, [load]);

    useEffect(() => {
        const timer = window.setInterval(() => void load(), AUTO_REFRESH_MS);
        return () => window.clearInterval(timer);
    }, [load]);

    const toggleMetric = useCallback((metric: MetricType) => {
        setMetrics((current) => {
            if (current.includes(metric)) return current.length > 1 ? current.filter((m) => m !== metric) : current;
            return ALL_METRICS.filter((m) => m === metric || current.includes(m));
        });
    }, []);

    const saveView = useCallback((view: SavedView) => {
        setSavedView(view);
        setActiveView(view.name);
        try {
            localStorage.setItem(STORAGE_KEY, JSON.stringify(view));
        } catch {}
    }, []);

    const visibleWidgets = widgets.filter((w) => w.visible && (w.id === 'kpis' || metrics.includes(WIDGET_METRIC[w.id])));

    return {
        range,
        setRange,
        presets,
        minDate: DASHBOARD_MIN_DATE,
        maxDate: DASHBOARD_MAX_DATE,
        hospitals,
        setHospitals,
        metrics,
        toggleMetric,
        data,
        loading,
        lastUpdated,
        refresh: load,
        widgets,
        visibleWidgets,
        savedView,
        activeView,
        setActiveView,
        saveView,
    };
}
