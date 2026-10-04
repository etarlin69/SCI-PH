import { useState } from 'react';
import { AlertTriangle, Stethoscope } from 'lucide-react';
import type { ClinicService } from '../../../types';
import { clinicsService, errorMessage } from '../../../services';
import { EmptyState, ErrorState, LoadingSpinner, Switch, useToast } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { cn } from '../../../utils/cn';
import { CapacityBar } from './CapacityBar';

export interface ClinicServicesPanelProps {
  clinicId: string;
  canUpdate: boolean;
}

/** Pestaña Servicios: disponibilidad para traslados (Switch), capacidad ocupada/total y avisos. */
export function ClinicServicesPanel({ clinicId, canUpdate }: ClinicServicesPanelProps) {
  const toast = useToast();
  const { data, loading, error, reload, setData } = useAsync(() => clinicsService.listServices(clinicId), [clinicId]);
  const [pending, setPending] = useState<ReadonlySet<string>>(new Set());

  const toggle = async (s: ClinicService, disponible: boolean) => {
    setPending((p) => new Set(p).add(s.id));
    try {
      const updated = await clinicsService.setServiceAvailability(clinicId, s.id, disponible);
      setData((list) => list?.map((x) => (x.id === updated.id ? updated : x)));
      toast({
        title: disponible ? 'Servicio disponible' : 'Servicio no disponible',
        description: `${s.nombre}${disponible ? ' vuelve a ofrecerse en traslados.' : ' ya no se ofrecerá en traslados.'}`,
        tone: 'success',
      });
    } catch (e) {
      toast({ title: 'No se pudo cambiar la disponibilidad', description: errorMessage(e), tone: 'error' });
    } finally {
      setPending((p) => {
        const next = new Set(p);
        next.delete(s.id);
        return next;
      });
    }
  };

  if (loading && !data) {
    return (
      <div className="flex justify-center py-12">
        <LoadingSpinner label="Cargando servicios…" />
      </div>
    );
  }
  if (error) return <ErrorState title="No se pudieron cargar los servicios" message={error} onRetry={reload} />;
  if (!data || data.length === 0) {
    return (
      <EmptyState
        icon={Stethoscope}
        title="Sin servicios registrados"
        description="Esta clínica todavía no tiene servicios configurados para traslados."
      />
    );
  }

  const disponibles = data.filter((s) => s.disponible).length;

  return (
    <div className="flex flex-col gap-3">
      <p className="text-[13px] text-ink-muted">
        <span className="tabular font-semibold text-ink-2">
          {disponibles} de {data.length} disponibles.
        </span>{' '}
        Los servicios marcados como no disponibles no se ofrecen al coordinar traslados.
      </p>
      <ul className="flex flex-col divide-y divide-line-soft rounded-lg border border-line">
        {data.map((s) => {
          const busy = pending.has(s.id);
          return (
            <li
              key={s.id}
              className="grid grid-cols-1 gap-3 px-4 py-3.5 sm:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)_auto] sm:items-center sm:gap-6"
            >
              <span className="flex min-w-0 flex-col gap-0.5">
                <span className={cn('text-sm font-semibold', s.disponible ? 'text-ink' : 'text-ink-3')}>{s.nombre}</span>
                <span className={cn('flex items-center gap-1 text-xs', s.alerta ? 'text-danger-text' : 'text-ink-muted')}>
                  {s.alerta && <AlertTriangle size={12} aria-hidden="true" className="shrink-0" />}
                  {s.nota ?? (s.disponible ? 'Operativo' : 'No disponible para traslados')}
                </span>
              </span>
              <span className="min-w-0">
                {s.capacidad && s.disponible && (
                  <CapacityBar ocupadas={s.capacidad.ocupadas} total={s.capacidad.total} unidad={s.capacidad.unidad} label={s.nombre} />
                )}
              </span>
              <span className="flex items-center justify-between gap-3 sm:justify-end">
                <span aria-hidden="true" className={cn('text-[13px] font-medium', s.disponible ? 'text-success-600' : 'text-ink-muted')}>
                  {s.disponible ? 'Disponible' : 'No disponible'}
                </span>
                <Switch
                  hideLabel
                  label={`${s.nombre} disponible`}
                  checked={s.disponible}
                  disabled={!canUpdate || busy}
                  onChange={(v) => void toggle(s, v)}
                />
              </span>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
