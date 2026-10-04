import { useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Plus, SearchX, UserPlus } from 'lucide-react';
import { useAuth, useCan } from '../../../auth';
import { Button, EmptyState, ErrorState, PageHeader, useToast } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { useDebouncedValue } from '../../../hooks/useDebouncedValue';
import { errorMessage, rolesService, usersService } from '../../../services';
import type { User, UserFilters as Filters } from '../../../types';
import { DeleteUserDialog } from '../components/DeleteUserDialog';
import { UserFilters } from '../components/UserFilters';
import { UserFormModal } from '../components/UserFormModal';
import { UserStatusDialog } from '../components/UserStatusDialog';
import { UsersTable } from '../components/UsersTable';
import { EMPTY_USER_FILTERS, filterUsers } from '../filters';

/** Gestión de cuentas: alta con contraseña temporal, edición, suspensión y baja. */
export function UsersPage() {
  const navigate = useNavigate();
  const toast = useToast();
  const { user: me } = useAuth();
  const canCreate = useCan('users', 'create');
  const canUpdate = useCan('users', 'update');
  const canDelete = useCan('users', 'delete');

  const usersQ = useAsync(() => usersService.list());
  const rolesQ = useAsync(() => rolesService.list());
  const users = useMemo(() => usersQ.data ?? [], [usersQ.data]);

  const [filters, setFilters] = useState<Filters>(EMPTY_USER_FILTERS);
  const debouncedQ = useDebouncedValue(filters.q);
  const applied = { ...filters, q: debouncedQ };
  const filtered = useMemo(() => filterUsers(users, applied), [users, applied.q, applied.rol, applied.estado]); // eslint-disable-line react-hooks/exhaustive-deps

  const [form, setForm] = useState<{ open: boolean; user?: User }>({ open: false });
  const [statusTarget, setStatusTarget] = useState<User | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<User | null>(null);

  const replace = (u: User) => usersQ.setData((prev) => prev?.map((x) => (x.id === u.id ? u : x)));

  const toggleStatus = async (u: User) => {
    const next = u.estado === 'Suspendido' ? 'Activo' : 'Suspendido';
    try {
      replace(await usersService.setStatus(u.id, next));
      toast({ title: next === 'Suspendido' ? 'Usuario suspendido' : 'Usuario reactivado', description: u.nombre, tone: 'success' });
    } catch (e) {
      toast({ title: 'No se pudo cambiar el estado', description: errorMessage(e), tone: 'error' });
    } finally {
      setStatusTarget(null);
    }
  };

  const remove = async (u: User) => {
    try {
      await usersService.remove(u.id);
      usersQ.setData((prev) => prev?.filter((x) => x.id !== u.id));
      toast({ title: 'Cuenta eliminada', description: `${u.nombre}. Su historial de auditoría se conserva.`, tone: 'success' });
    } catch (e) {
      toast({ title: 'No se pudo eliminar', description: errorMessage(e), tone: 'error' });
    } finally {
      setDeleteTarget(null);
    }
  };

  const clear = () => setFilters(EMPTY_USER_FILTERS);
  const loading = usersQ.loading || rolesQ.loading;

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Usuarios"
        description={
          <>
            Cuentas con acceso al sistema. Cada usuario tiene un rol que define sus permisos (ver{' '}
            <Link to="/admin/roles" className="font-semibold text-primary-600 hover:underline">
              Roles y permisos
            </Link>
            ).
          </>
        }
        actions={
          canCreate && (
            <Button leftIcon={<Plus size={16} aria-hidden="true" />} onClick={() => setForm({ open: true })}>
              Nuevo usuario
            </Button>
          )
        }
      />

      <section aria-label="Lista de usuarios" className="flex flex-col gap-4 rounded-[10px] border border-line bg-white p-4 sm:p-5">
        <UserFilters value={filters} onChange={setFilters} users={users} roles={rolesQ.data ?? []} resultCount={filtered.length} />
        {usersQ.error ? (
          <ErrorState
            title="No se pudieron cargar los usuarios"
            message={`${usersQ.error} Tus filtros se mantienen; vuelve a intentarlo en unos segundos.`}
            onRetry={usersQ.reload}
          />
        ) : (
          <UsersTable
            users={filtered}
            loading={loading}
            selfId={me?.id ?? ''}
            canUpdate={canUpdate}
            canDelete={canDelete}
            onOpen={(u) => navigate(`/admin/usuarios/${u.id}`)}
            onEdit={(u) => setForm({ open: true, user: u })}
            onToggleStatus={setStatusTarget}
            onDelete={setDeleteTarget}
            resetPageKey={`${applied.q}|${applied.rol}|${applied.estado}`}
            emptyState={
              users.length === 0 ? (
                <EmptyState
                  icon={UserPlus}
                  title="Todavía no hay usuarios"
                  description="Crea la primera cuenta y asígnale un rol. Recibirá una contraseña temporal para su primer acceso."
                  action={canCreate && <Button onClick={() => setForm({ open: true })}>Nuevo usuario</Button>}
                />
              ) : (
                <EmptyState
                  icon={SearchX}
                  title="Ningún usuario coincide con los filtros"
                  description="Prueba con otro nombre o email, o quita el filtro de rol o estado."
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
      </section>

      <UserFormModal
        open={form.open}
        user={form.user}
        roles={rolesQ.data ?? []}
        onClose={() => setForm({ open: false })}
        onCreate={(input) => usersService.create(input)}
        onUpdate={(id, input) => usersService.update(id, input)}
        onSaved={(u, created, delivery) => {
          setForm({ open: false });
          if (created) usersQ.setData((prev) => [u, ...(prev ?? [])]);
          else replace(u);
          toast({
            title: created ? 'Usuario creado' : 'Cambios guardados',
            description: created
              ? delivery === 'email'
                ? `Contraseña temporal enviada a ${u.email}.`
                : 'Entrega la contraseña copiada por un canal seguro. Caduca en 72 h.'
              : u.nombre,
            tone: 'success',
          });
        }}
      />

      <UserStatusDialog user={statusTarget} onConfirm={toggleStatus} onCancel={() => setStatusTarget(null)} />
      <DeleteUserDialog
        user={deleteTarget}
        onConfirm={remove}
        onCancel={() => setDeleteTarget(null)}
        onSuspendInstead={(u) => {
          setDeleteTarget(null);
          setStatusTarget(u);
        }}
      />
    </div>
  );
}

export default UsersPage;
