import { useCallback, useMemo, useState } from 'react';
import { Lock, ScrollText, SearchX } from 'lucide-react';
import type { AuditEvent, AuditExportFormat, AuditFilters } from '../../../types';
import { auditService, errorMessage } from '../../../services';
import { Permissions, useCan } from '../../../auth';
import { Badge, Button, EmptyState, ErrorState, PageHeader, useToast } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { useDebouncedValue } from '../../../hooks/useDebouncedValue';
import { useIsMobile } from '../../../hooks/useMediaQuery';
import { formatNumber } from '../../../utils/formatters';
import { formatDMY } from '../../../utils/dates';
import {
  applyAuditFilters,
  auditMinDate,
  auditPresets,
  countActiveFilters,
  defaultAuditFilters,
  hasAnyFilter,
  isDefaultRange,
  rangeLabel,
  resultCounts,
} from '../auditFilters';
import { ActiveFilterChips, type FilterChip } from '../components/ActiveFilterChips';
import { AuditEventModal } from '../components/AuditEventModal';
import { AuditFilterBar } from '../components/AuditFilterBar';
import { AuditFiltersSheet } from '../components/AuditFiltersSheet';
import { AuditTable } from '../components/AuditTable';
import { ExportMenu } from '../components/ExportMenu';

/** Registro de auditoría (solo anexar): consulta filtrable de eventos, detalle y exportación. */
export function AuditPage() {
  const isMobile = useIsMobile();
  const toast = useToast();
  const canExport = useCan(Permissions.Audit.Read);

  const events = useAsync(() => auditService.list(), []);
  const facets = useAsync(() => auditService.facets(), []);

  const defaults = useMemo(() => defaultAuditFilters(), []);
  const presets = useMemo(() => auditPresets(), []);
  const minDate = useMemo(() => auditMinDate(), []);

  const [filters, setFilters] = useState<AuditFilters>(defaults);
  const [sheetOpen, setSheetOpen] = useState(false);
  const [selected, setSelected] = useState<AuditEvent | null>(null);

  const debouncedQ = useDebouncedValue(filters.q, 200);
  const effective = useMemo(() => ({ ...filters, q: debouncedQ }), [filters, debouncedQ]);

  const all = useMemo(() => events.data ?? [], [events.data]);
  const filtered = useMemo(() => applyAuditFilters(all, effective), [all, effective]);
  const counts = useMemo(() => resultCounts(all, effective), [all, effective]);
  const countFor = useCallback((f: AuditFilters) => applyAuditFilters(all, { ...f, q: debouncedQ }).length, [all, debouncedQ]);

  const users = facets.data?.usuarios ?? [...new Set(all.map((e) => e.usuario))];
  const actions = facets.data?.acciones ?? [];

  const patch = (p: Partial<AuditFilters>) => setFilters((f) => ({ ...f, ...p }));
  const clear = () => setFilters(defaults);
  const hasFilters = hasAnyFilter(filters, defaults);
  const activeCount = countActiveFilters(filters, defaults);

  const chips: FilterChip[] = [];
  if (filters.q.trim()) chips.push({ key: 'q', label: `«${filters.q.trim()}»`, onRemove: () => patch({ q: '' }) });
  if (!isDefaultRange(filters, defaults))
    chips.push({ key: 'range', label: rangeLabel(filters, presets), onRemove: () => patch({ from: defaults.from, to: defaults.to }) });
  if (filters.usuario) chips.push({ key: 'usuario', label: filters.usuario, onRemove: () => patch({ usuario: '' }) });
  if (filters.accion) chips.push({ key: 'accion', label: filters.accion, onRemove: () => patch({ accion: '' }) });
  if (filters.resultado) chips.push({ key: 'resultado', label: filters.resultado, onRemove: () => patch({ resultado: '' }) });

  const runExport = async (format: AuditExportFormat): Promise<boolean> => {
    try {
      const result = await auditService.export(format, filtered, effective);
      const period = `${formatDMY(effective.from)} – ${formatDMY(effective.to)}`;
      if (format === 'csv') {
        toast({
          title: 'CSV exportado',
          description: `${formatNumber(result.count)} eventos · ${period}. La exportación queda registrada en auditoría.`,
        });
      } else {
        // El servicio aún no genera el PDF (placeholder hasta la integración con el backend).
        toast({
          tone: 'info',
          title: 'Exportación PDF aún no disponible',
          description: `La solicitud de ${formatNumber(result.count)} eventos (${period}) quedó registrada en auditoría, pero el PDF se generará en el servidor cuando esté integrado. Usa «Exportar CSV» mientras tanto.`,
          durationMs: 6000,
        });
      }
      // La exportación añade un evento al registro: refresca la lista sin mostrar el esqueleto.
      auditService.list().then(events.setData, () => undefined);
      return true;
    } catch (e) {
      toast({ tone: 'error', title: 'No se pudo exportar', description: errorMessage(e) });
      return false;
    }
  };

  const emptyState =
    all.length === 0 ? (
      <EmptyState
        icon={ScrollText}
        title="Aún no hay registros"
        description="Los inicios de sesión, cambios y accesos a datos aparecerán aquí automáticamente en cuanto se produzcan."
      />
    ) : (
      <EmptyState
        icon={SearchX}
        title="Sin eventos para estos filtros"
        description={isMobile ? 'Amplía el periodo o quita algún filtro.' : 'Amplía el rango de fechas o quita algún filtro.'}
        action={
          <Button variant="secondary" onClick={clear}>
            Limpiar filtros
          </Button>
        }
      />
    );

  const exportDisabled = !canExport || events.loading || !!events.error || filtered.length === 0;

  return (
    <div className="flex flex-col gap-5 sm:gap-6">
      <PageHeader
        title="Registro de auditoría"
        meta={
          <Badge tone="neutral" dot={false}>
            <Lock size={12} aria-hidden="true" />
            Registro inalterable
          </Badge>
        }
        description={
          <span className="inline-flex items-start gap-1.5">
            <Lock size={14} className="mt-0.5 shrink-0" aria-hidden="true" />
            Registro inmutable (solo anexar): los eventos no se pueden editar ni eliminar.
          </span>
        }
        actions={
          <ExportMenu
            count={filtered.length}
            disabled={exportDisabled}
            disabledReason={!canExport ? 'Necesitas permiso de lectura de auditoría para exportar.' : 'No hay eventos que exportar.'}
            onExport={runExport}
            mobile={isMobile}
          />
        }
      />

      <section aria-label="Eventos de auditoría" className="flex flex-col gap-4 rounded-[10px] border border-line bg-white p-4 sm:p-5">
        <AuditFilterBar
          filters={filters}
          onChange={patch}
          users={users}
          actions={actions}
          counts={counts}
          presets={presets}
          minDate={minDate}
          hasFilters={hasFilters}
          onClear={clear}
          activeCount={activeCount}
          onOpenSheet={() => setSheetOpen(true)}
          mobile={isMobile}
        />
        <ActiveFilterChips chips={chips} onClearAll={isMobile ? clear : undefined} />

        {events.error ? (
          <ErrorState
            title="No se pudo cargar el registro de auditoría"
            message={`${events.error} Los eventos se siguen registrando; solo falló la consulta.`}
            onRetry={events.reload}
          />
        ) : (
          <>
            <div className="flex items-center justify-between gap-3 text-[13px] text-ink-muted" aria-live="polite">
              <span className="tabular">
                {events.loading
                  ? 'Cargando eventos…'
                  : `${formatNumber(filtered.length)} eventos · ${formatDMY(effective.from)} – ${formatDMY(effective.to)}`}
              </span>
              {isMobile && !events.loading && filtered.length > 0 && <span aria-hidden="true">Desliza para ver más →</span>}
            </div>
            <AuditTable
              events={filtered}
              loading={events.loading}
              onOpen={setSelected}
              emptyState={emptyState}
              resetPageKey={effective}
              compact={isMobile}
            />
          </>
        )}
      </section>

      <AuditFiltersSheet
        open={sheetOpen}
        onClose={() => setSheetOpen(false)}
        value={filters}
        defaults={defaults}
        onApply={(f) => {
          setFilters(f);
          setSheetOpen(false);
        }}
        countFor={countFor}
        users={users}
        actions={actions}
        presets={presets}
        minDate={minDate}
      />
      <AuditEventModal event={selected} onClose={() => setSelected(null)} mobile={isMobile} />
    </div>
  );
}

export default AuditPage;
