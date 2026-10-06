import { useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Plus, SearchX, UsersRound } from 'lucide-react';
import { useCanAny, INVITABLE_STAFF_ROLES } from '../../../auth';
import { DEMO_ADMIN_PERMISSIONS } from '../../../components/common/navigation';
import { Button, EmptyState, ErrorState, PageHeader, useToast } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { useDebouncedValue } from '../../../hooks/useDebouncedValue';
import { clinicsService, errorMessage, staffService } from '../../../services';
import type { StaffFilters as Filters, StaffMember } from '../../../types';
import { DeleteStaffDialog } from '../components/DeleteStaffDialog';
import { StaffFilters } from '../components/StaffFilters';
import { StaffFormModal } from '../components/StaffFormModal';
import { StaffTable } from '../components/StaffTable';
import { EMPTY_STAFF_FILTERS, filterStaff } from '../filters';

/** Perfil laboral de cada persona: puesto, clínica y licencias. */
export function StaffPage() {
  const navigate = useNavigate();
  const toast = useToast();
  const canCreate = useCanAny(DEMO_ADMIN_PERMISSIONS);
  const canUpdate = useCanAny(DEMO_ADMIN_PERMISSIONS);
  const canDelete = useCanAny(DEMO_ADMIN_PERMISSIONS);

  const { data, loading, error, reload, setData } = useAsync(() => staffService.list());
  const clinicsQ = useAsync(() => clinicsService.options());
  const staff = useMemo(() => data ?? [], [data]);
  const clinics = useMemo(() => clinicsQ.data ?? [], [clinicsQ.data]);
  const clinicName = (id: string) => clinics.find((c) => c.id === id)?.nombre ?? id;

  const [filters, setFilters] = useState<Filters>(EMPTY_STAFF_FILTERS);
  const q = useDebouncedValue(filters.q);
  const filtered = useMemo(() => filterStaff(staff, { ...filters, q }), [staff, filters, q]);
  const [form, setForm] = useState<{ open: boolean; member?: StaffMember }>({ open: false });
  const [toDelete, setToDelete] = useState<StaffMember | null>(null);

  const replace = (m: StaffMember) => setData((prev) => prev?.map((x) => (x.id === m.id ? m : x)));

  const remove = async (m: StaffMember, suspendAccount: boolean) => {
    try {
      await staffService.remove(m.id, { suspendAccount });
      setData((prev) => prev?.filter((x) => x.id !== m.id));
      toast({
        title: 'Perfil eliminado',
        description: suspendAccount ? `${m.nombre}. Su cuenta de usuario quedó suspendida.` : m.nombre,
        tone: 'success',
      });
    } catch (e) {
      toast({ title: 'No se pudo eliminar', description: errorMessage(e), tone: 'error' });
    } finally {
      setToDelete(null);
    }
  };

  const deactivate = async (m: StaffMember) => {
    setToDelete(null);
    try {
      replace(await staffService.update(m.id, { ...m, estado: 'Inactivo' }));
      toast({ title: 'Marcado como inactivo', description: `${m.nombre} no aparecerá en turnos ni asignaciones.`, tone: 'success' });
    } catch (e) {
      toast({ title: 'No se pudo actualizar', description: errorMessage(e), tone: 'error' });
    }
  };

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Personal"
        description={
          <>
            Perfil laboral de cada persona: puesto, clínica y licencias. El acceso al sistema se gestiona en{' '}
            <Link to="/admin/usuarios" className="font-semibold text-primary-600 hover:underline">
              Usuarios
            </Link>
            .
          </>
        }
        actions={
          canCreate && (
            <Button leftIcon={<Plus size={16} aria-hidden="true" />} onClick={() => setForm({ open: true })}>
              Nuevo personal
            </Button>
          )
        }
      />
      <section aria-label="Lista de personal" className="flex flex-col gap-4 rounded-[10px] border border-line bg-white p-4 sm:p-5">
        <StaffFilters value={filters} onChange={setFilters} staff={staff} clinics={clinics} resultCount={filtered.length} />
        {error ? (
          <ErrorState title="No se pudo cargar el personal" message={error} onRetry={reload} />
        ) : (
          <StaffTable
            staff={filtered}
            clinicName={clinicName}
            loading={loading}
            canUpdate={canUpdate}
            canDelete={canDelete}
            onOpen={(m) => navigate(`/admin/personal/${m.id}`)}
            onEdit={(member) => setForm({ open: true, member })}
            onDelete={setToDelete}
            resetPageKey={`${q}|${filters.puesto}|${filters.clinicId}|${filters.estado}`}
            emptyState={
              staff.length === 0 ? (
                <EmptyState
                  icon={UsersRound}
                  title="Aún no hay personal registrado"
                  description="Registra a médicos, enfermería, paramédicos y operadores, y asígnales una clínica."
                  action={canCreate && <Button onClick={() => setForm({ open: true })}>Nuevo personal</Button>}
                />
              ) : (
                <EmptyState
                  icon={SearchX}
                  title="Nadie coincide con los filtros"
                  action={
                    <Button variant="secondary" onClick={() => setFilters(EMPTY_STAFF_FILTERS)}>
                      Limpiar filtros
                    </Button>
                  }
                />
              )
            }
          />
        )}
      </section>

      <StaffFormModal
        open={form.open}
        member={form.member}
        clinics={clinics}
        roles={INVITABLE_STAFF_ROLES}
        onClose={() => setForm({ open: false })}
        onSubmit={(input, access) => (form.member ? staffService.update(form.member.id, input) : staffService.create(input, access))}
        onSaved={(m, created, invited) => {
          setForm({ open: false });
          if (created) setData((prev) => [m, ...(prev ?? [])]);
          else replace(m);
          toast({
            title: created ? 'Personal registrado' : 'Cambios guardados',
            description: created
              ? invited
                ? `Invitación enviada a ${m.email}. Aparecerá en Invitaciones.`
                : `${m.nombre} sin acceso al sistema.`
              : m.nombre,
            tone: 'success',
          });
        }}
      />
      <DeleteStaffDialog
        member={toDelete}
        onConfirm={remove}
        onDeactivateInstead={canUpdate ? deactivate : undefined}
        onCancel={() => setToDelete(null)}
      />
    </div>
  );
}

export default StaffPage;
