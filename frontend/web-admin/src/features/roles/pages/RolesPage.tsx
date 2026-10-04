import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { Grid3x3, Info, Plus, SearchX } from 'lucide-react';
import { useCan } from '../../../auth';
import {
  Button,
  ConfirmDialog,
  EmptyState,
  ErrorState,
  PageHeader,
  SearchInput,
  SegmentedFilter,
  useToast,
} from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { errorMessage, rolesService } from '../../../services';
import type { Role, RoleType } from '../../../types';
import { RoleFormModal } from '../components/RoleFormModal';
import { RolesTable } from '../components/RolesTable';

type TypeFilter = 'all' | RoleType;

/** Listado de roles: crear personalizados, editar su nombre y eliminarlos si no tienen usuarios. */
export function RolesPage() {
  const toast = useToast();
  const canCreate = useCan('roles', 'create');
  const canEdit = useCan('roles', 'update');
  const canDelete = useCan('roles', 'delete');
  const { data, loading, error, reload, setData } = useAsync(() => rolesService.list());
  const roles = useMemo(() => data ?? [], [data]);

  const [q, setQ] = useState('');
  const [tipo, setTipo] = useState<TypeFilter>('all');
  const [form, setForm] = useState<{ open: boolean; role?: Role }>({ open: false });
  const [toDelete, setToDelete] = useState<Role | null>(null);

  const filtered = useMemo(() => {
    const term = q.trim().toLowerCase();
    return roles.filter(
      (r) => (tipo === 'all' || r.tipo === tipo) && (!term || `${r.nombre} ${r.descripcion}`.toLowerCase().includes(term)),
    );
  }, [roles, q, tipo]);

  const clear = () => {
    setQ('');
    setTipo('all');
  };

  const confirmDelete = async () => {
    if (!toDelete) return;
    try {
      await rolesService.remove(toDelete.id);
      setData((prev) => prev?.filter((r) => r.id !== toDelete.id));
      toast({ title: 'Rol eliminado', description: `«${toDelete.nombre}» ya no está disponible para asignar.`, tone: 'success' });
    } catch (e) {
      toast({ title: 'No se pudo eliminar el rol', description: errorMessage(e), tone: 'error' });
    } finally {
      setToDelete(null);
    }
  };

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Roles y permisos"
        description="Cada usuario tiene un rol. El rol define qué puede leer, crear, actualizar y eliminar en cada módulo del portal."
        actions={
          <>
            <Link
              to={`/admin/roles/${roles[0]?.id ?? 'ROL-ADM'}/permisos`}
              className="inline-flex h-10 items-center gap-2 rounded-lg border border-line bg-white px-4 text-sm font-semibold text-ink-2 no-underline hover:bg-surface-hover"
            >
              <Grid3x3 size={16} aria-hidden="true" />
              Matriz de permisos
            </Link>
            {canCreate && (
              <Button leftIcon={<Plus size={16} aria-hidden="true" />} onClick={() => setForm({ open: true })}>
                Nuevo rol
              </Button>
            )}
          </>
        }
      />

      <div className="flex gap-3 rounded-lg border border-primary-200 bg-primary-50 px-4 py-3 text-[13px] text-primary-700">
        <Info size={18} className="mt-0.5 shrink-0" aria-hidden="true" />
        <p>
          <strong>Los roles del sistema son de solo lectura.</strong> Si necesitas una combinación distinta, crea un rol personalizado a
          partir de uno existente. Ningún rol puede modificar ni borrar registros de auditoría.
        </p>
      </div>

      <section className="flex flex-col gap-4 rounded-[10px] border border-line bg-white p-4 sm:p-5" aria-label="Lista de roles">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <SegmentedFilter<TypeFilter>
            label="Filtrar por tipo"
            value={tipo}
            onChange={setTipo}
            options={[
              { value: 'all', label: 'Todos', count: roles.length },
              { value: 'Sistema', label: 'Sistema', count: roles.filter((r) => r.tipo === 'Sistema').length },
              { value: 'Personalizado', label: 'Personalizados', count: roles.filter((r) => r.tipo === 'Personalizado').length },
            ]}
          />
          <SearchInput label="Buscar roles" placeholder="Buscar por nombre o descripción" value={q} onChange={setQ} />
        </div>

        {error ? (
          <ErrorState title="No se pudieron cargar los roles" message={error} onRetry={reload} />
        ) : (
          <RolesTable
            roles={filtered}
            loading={loading}
            canEdit={canEdit}
            canDelete={canDelete}
            onEdit={(role) => setForm({ open: true, role })}
            onDelete={setToDelete}
            resetPageKey={`${q}|${tipo}`}
            emptyState={
              roles.length === 0 ? (
                <EmptyState title="No hay roles" description="Crea el primer rol personalizado." />
              ) : (
                <EmptyState
                  icon={SearchX}
                  title="Ningún rol coincide con los filtros"
                  action={
                    <Button variant="secondary" onClick={clear}>
                      Limpiar filtros
                    </Button>
                  }
                />
              )
            }
          />
        )}
        <p className="text-xs text-ink-muted">
          Permisos del Portal de Administración (10 módulos × 4 acciones; 5 combinaciones no aplican, p. ej. modificar auditoría). Los
          permisos clínicos de las apps móviles se definen en cada app.
        </p>
      </section>

      <RoleFormModal
        open={form.open}
        role={form.role}
        roles={roles}
        onClose={() => setForm({ open: false })}
        onSubmit={(input) => (form.role ? rolesService.update(form.role.id, input) : rolesService.create(input))}
        onSaved={(role) => {
          setForm({ open: false });
          setData((prev) =>
            prev?.some((r) => r.id === role.id) ? prev.map((r) => (r.id === role.id ? role : r)) : [...(prev ?? []), role],
          );
          toast({
            title: form.role ? 'Rol actualizado' : 'Rol creado',
            description: form.role ? undefined : 'Configura sus permisos en la matriz.',
            tone: 'success',
          });
        }}
      />

      <ConfirmDialog
        open={!!toDelete}
        title={`¿Eliminar el rol «${toDelete?.nombre ?? ''}»?`}
        message={
          toDelete && toDelete.usuarios > 0
            ? `Tiene ${toDelete.usuarios} usuario(s) asignado(s). Reasígnalos a otro rol antes de eliminarlo.`
            : 'No tiene usuarios asignados. Se eliminarán sus permisos; esta acción no se puede deshacer y queda registrada en auditoría.'
        }
        confirmLabel="Eliminar rol"
        onConfirm={confirmDelete}
        onCancel={() => setToDelete(null)}
      />
    </div>
  );
}

export default RolesPage;
