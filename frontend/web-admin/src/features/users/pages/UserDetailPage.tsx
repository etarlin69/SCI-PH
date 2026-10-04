import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { Mail, Pencil, PauseCircle, PlayCircle, Trash2, UserX } from 'lucide-react';
import { useAuth, useCan } from '../../../auth';
import { useBreadcrumb } from '../../../components/common';
import { Avatar, Button, ConfirmDialog, EmptyState, ErrorState, LoadingSpinner, useToast } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { errorMessage, isServiceError, rolesService, usersService } from '../../../services';
import type { User } from '../../../types';
import { DeleteUserDialog } from '../components/DeleteUserDialog';
import { UserActivityList } from '../components/UserActivityList';
import { UserFormModal } from '../components/UserFormModal';
import { UserSecurityPanel } from '../components/UserSecurityPanel';
import { UserSessionsCard } from '../components/UserSessionsCard';
import { UserStatusBadge } from '../components/UserStatusBadge';
import { UserStatusDialog } from '../components/UserStatusDialog';
import { UserSummaryCard } from '../components/UserSummaryCard';

/** Ficha de usuario (`/admin/usuarios/:id`): cuenta, seguridad, sesiones activas y actividad reciente. */
export function UserDetailPage() {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const toast = useToast();
  const { user: me } = useAuth();
  const canUpdate = useCan('users', 'update');
  const canDelete = useCan('users', 'delete');
  const canViewStaff = useCan('staff', 'read');

  const userQ = useAsync(() => usersService.get(id), [id]);
  const rolesQ = useAsync(() => rolesService.list());
  const user = userQ.data;
  useBreadcrumb(user?.nombre);

  const [editOpen, setEditOpen] = useState(false);
  const [statusTarget, setStatusTarget] = useState<User | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<User | null>(null);
  const [resetOpen, setResetOpen] = useState(false);
  const [securityKey, setSecurityKey] = useState(0);

  if (userQ.loading) {
    return (
      <div className="flex justify-center py-24">
        <LoadingSpinner size="lg" label="Cargando usuario…" />
      </div>
    );
  }
  if (userQ.error || !user) {
    return userQ.error?.includes('no existe') ? (
      <EmptyState
        icon={UserX}
        titleAs="h1"
        title="Usuario no encontrado"
        description="La cuenta no existe o fue eliminada. Su historial sigue disponible en Auditoría."
        action={
          <Link to="/admin/usuarios" className="font-semibold text-primary-600 hover:underline">
            Volver a Usuarios
          </Link>
        }
      />
    ) : (
      <ErrorState title="No se pudo cargar el usuario" message={userQ.error ?? ''} onRetry={userQ.reload} />
    );
  }

  const self = user.id === me?.id;
  const suspended = user.estado === 'Suspendido';

  const toggleStatus = async (u: User) => {
    try {
      userQ.setData(await usersService.setStatus(u.id, suspended ? 'Activo' : 'Suspendido'));
      toast({
        title: suspended ? 'Usuario reactivado' : 'Usuario suspendido',
        description: suspended ? undefined : 'Se cerraron sus sesiones activas.',
        tone: 'success',
      });
    } catch (e) {
      toast({ title: 'No se pudo cambiar el estado', description: errorMessage(e), tone: 'error' });
    } finally {
      setStatusTarget(null);
    }
  };

  const remove = async (u: User) => {
    try {
      await usersService.remove(u.id);
      toast({ title: 'Cuenta eliminada', description: `${u.nombre}. Su historial de auditoría se conserva.`, tone: 'success' });
      navigate('/admin/usuarios', { replace: true });
    } catch (e) {
      toast({ title: 'No se pudo eliminar', description: errorMessage(e), tone: 'error' });
      setDeleteTarget(null);
    }
  };

  const sendReset = async () => {
    try {
      await usersService.sendPasswordReset(user.id);
      setSecurityKey((k) => k + 1);
      toast({ title: 'Enlace enviado', description: `Restablecimiento enviado a ${user.email}.`, tone: 'success' });
    } catch (e) {
      toast({
        title: isServiceError(e, 'RATE_LIMITED') ? 'Espera unos minutos' : 'No se pudo enviar el enlace',
        description: errorMessage(e),
        tone: 'error',
      });
    } finally {
      setResetOpen(false);
    }
  };

  return (
    <div className="flex flex-col gap-5">
      <Link to="/admin/usuarios" className="w-fit text-[13px] font-medium text-primary-600 hover:underline">
        ← Usuarios
      </Link>

      <header className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
        <div className="flex min-w-0 items-center gap-4">
          <Avatar name={user.nombre} size="lg" />
          <div className="flex min-w-0 flex-col gap-1">
            <div className="flex flex-wrap items-center gap-2.5">
              <h1 className="truncate text-xl font-semibold text-ink sm:text-2xl">{user.nombre}</h1>
              <UserStatusBadge estado={user.estado} />
              {self && <span className="rounded bg-primary-100 px-1.5 text-[11px] font-semibold text-primary-600">Tú</span>}
            </div>
            <p className="break-all text-[13px] text-ink-muted">
              {user.email} · {user.rol} · <span className="font-mono">{user.id}</span>
            </p>
          </div>
        </div>
        {canUpdate && (
          <div className="flex flex-wrap gap-2">
            <Button variant="secondary" leftIcon={<Pencil size={16} aria-hidden="true" />} onClick={() => setEditOpen(true)}>
              Editar
            </Button>
            <Button variant="secondary" leftIcon={<Mail size={16} aria-hidden="true" />} onClick={() => setResetOpen(true)}>
              Restablecer contraseña
            </Button>
            <Button
              variant={suspended ? 'secondary' : 'danger'}
              leftIcon={suspended ? <PlayCircle size={16} aria-hidden="true" /> : <PauseCircle size={16} aria-hidden="true" />}
              onClick={() => setStatusTarget(user)}
              disabled={self}
              title={self ? 'No puedes suspender tu propia cuenta' : undefined}
            >
              {suspended ? 'Reactivar' : 'Suspender'}
            </Button>
            {canDelete && (
              <Button
                variant="ghost"
                leftIcon={<Trash2 size={16} aria-hidden="true" />}
                onClick={() => setDeleteTarget(user)}
                disabled={self}
                title={self ? 'No puedes eliminar tu propia cuenta' : undefined}
              >
                Eliminar
              </Button>
            )}
          </div>
        )}
      </header>

      <div className="grid grid-cols-1 gap-5 xl:grid-cols-2">
        <UserSummaryCard user={user} canViewStaff={canViewStaff} />
        <UserSecurityPanel
          key={securityKey}
          user={user}
          canUpdate={canUpdate}
          onMfaReset={() => userQ.setData((u) => (u ? { ...u, mfaActivo: false } : u))}
        />
        <div className="xl:col-span-2">
          <UserSessionsCard user={user} canUpdate={canUpdate && !suspended} />
        </div>
        <div className="xl:col-span-2">
          <UserActivityList userId={user.id} email={user.email} />
        </div>
      </div>

      <UserFormModal
        open={editOpen}
        user={user}
        roles={rolesQ.data ?? []}
        emailEditable={false}
        onClose={() => setEditOpen(false)}
        onCreate={(input) => usersService.create(input)}
        onUpdate={(uid, input) => usersService.update(uid, input)}
        onSaved={(u) => {
          setEditOpen(false);
          userQ.setData(u);
          toast({ title: 'Cambios guardados', description: u.nombre, tone: 'success' });
        }}
      />
      <ConfirmDialog
        open={resetOpen}
        tone="primary"
        title="¿Restablecer la contraseña?"
        message={`Se enviará un enlace de un solo uso a ${user.email}. La contraseña nunca se muestra ni se envía en claro.`}
        confirmLabel="Enviar enlace"
        onConfirm={sendReset}
        onCancel={() => setResetOpen(false)}
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

export default UserDetailPage;
