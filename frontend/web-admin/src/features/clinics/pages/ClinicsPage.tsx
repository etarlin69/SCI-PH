import { useMemo, useState } from 'react';
import { Building2, Plus, SearchX } from 'lucide-react';
import type { Clinic } from '../../../types';
import { clinicsService } from '../../../services';
import { useCanAny } from '../../../auth';
import { DEMO_ADMIN_PERMISSIONS } from '../../../components/common/navigation';
import { Button, Card, EmptyState, ErrorState, PageHeader } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { useDebouncedValue } from '../../../hooks/useDebouncedValue';
import { ClinicFilters } from '../components/ClinicFilters';
import { ClinicFormModal } from '../components/ClinicFormModal';
import { ClinicStatusDialog } from '../components/ClinicStatusDialog';
import { ClinicsTable } from '../components/ClinicsTable';
import { DeleteClinicDialog } from '../components/DeleteClinicDialog';
import { EMPTY_CLINIC_FILTERS, filterClinics, type ClinicListFilters } from '../filters';

type FormState = { open: false } | { open: true; clinic: Clinic | null };

/** Clínicas: directorio de hospitales, clínicas y centros que reciben pacientes (sustituye a «Hospitales»). */
export function ClinicsPage() {
  const canCreate = useCanAny(DEMO_ADMIN_PERMISSIONS);
  const canUpdate = useCanAny(DEMO_ADMIN_PERMISSIONS);
  const canDelete = useCanAny(DEMO_ADMIN_PERMISSIONS);

  const { data, loading, error, reload, setData } = useAsync(() => clinicsService.list(), []);
  const [filters, setFilters] = useState<ClinicListFilters>(EMPTY_CLINIC_FILTERS);
  const debouncedQ = useDebouncedValue(filters.q);
  const [form, setForm] = useState<FormState>({ open: false });
  const [statusTarget, setStatusTarget] = useState<Clinic | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<Clinic | null>(null);

  const clinics = useMemo(() => data ?? [], [data]);
  const applied = useMemo(() => ({ ...filters, q: debouncedQ }), [filters, debouncedQ]);
  const rows = useMemo(() => filterClinics(clinics, applied), [clinics, applied]);
  const takenNames = useMemo(() => clinics.map((c) => c.nombre), [clinics]);

  const replace = (c: Clinic) => setData((list) => list?.map((x) => (x.id === c.id ? c : x)));
  const clearFilters = () => setFilters(EMPTY_CLINIC_FILTERS);
  const openCreate = () => setForm({ open: true, clinic: null });

  const createButton = canCreate ? (
    <Button leftIcon={<Plus size={16} aria-hidden="true" />} onClick={openCreate}>
      Nueva clínica
    </Button>
  ) : null;

  const emptyAll = !loading && !error && clinics.length === 0;

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Clínicas"
        description="Hospitales, clínicas y centros que reciben pacientes. Sustituye al antiguo catálogo de hospitales."
        actions={createButton}
      />

      <Card padding="none" as="section" aria-label="Listado de clínicas" className="overflow-hidden">
        {error && !data ? (
          <ErrorState title="No se pudieron cargar las clínicas" message={error} onRetry={reload} />
        ) : emptyAll ? (
          <EmptyState
            icon={Building2}
            title="No hay clínicas registradas"
            description="Añade los centros que reciben pacientes para poder asignarles personal, dispositivos y traslados."
            action={createButton}
          />
        ) : (
          <>
            <div className="border-b border-line px-4 py-4 sm:px-5">
              <ClinicFilters value={filters} onChange={setFilters} clinics={clinics} />
            </div>
            {error && <ErrorState variant="banner" message={error} onRetry={reload} className="mx-4 mt-4 sm:mx-5" />}
            <div className="px-2 pb-1 pt-2 sm:px-3">
              <ClinicsTable
                rows={rows}
                loading={loading && !data}
                resetPageKey={applied}
                canUpdate={canUpdate}
                canDelete={canDelete}
                onEdit={(c) => setForm({ open: true, clinic: c })}
                onToggleStatus={setStatusTarget}
                onDelete={setDeleteTarget}
                emptyState={
                  <EmptyState
                    icon={SearchX}
                    title="Ninguna clínica coincide con los filtros"
                    description="Prueba con otra búsqueda o quita algún filtro."
                    action={
                      <Button variant="secondary" onClick={clearFilters}>
                        Limpiar filtros
                      </Button>
                    }
                  />
                }
              />
            </div>
          </>
        )}
      </Card>

      <ClinicFormModal
        open={form.open}
        clinic={form.open ? form.clinic : null}
        takenNames={takenNames}
        onClose={() => setForm({ open: false })}
        onSaved={(c, mode) => {
          setForm({ open: false });
          if (mode === 'create') {
            setFilters(EMPTY_CLINIC_FILTERS);
            reload();
          } else {
            replace(c);
          }
        }}
      />
      <ClinicStatusDialog
        clinic={statusTarget}
        onClose={() => setStatusTarget(null)}
        onChanged={(c) => {
          replace(c);
          setStatusTarget(null);
        }}
      />
      <DeleteClinicDialog
        clinic={deleteTarget}
        canDeactivate={canUpdate}
        onClose={() => setDeleteTarget(null)}
        onDeleted={(c) => {
          setData((list) => list?.filter((x) => x.id !== c.id));
          setDeleteTarget(null);
        }}
        onDeactivated={(c) => {
          replace(c);
          setDeleteTarget(null);
        }}
      />
    </div>
  );
}

export default ClinicsPage;
