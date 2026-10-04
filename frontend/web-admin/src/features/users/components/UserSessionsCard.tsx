import { useState } from 'react';
import { MonitorX } from 'lucide-react';
import type { User, UserSession } from '../../../types';
import { Button, Card, ConfirmDialog, EmptyState, ErrorState, useToast } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { errorMessage, usersService } from '../../../services';
import { RevokeAllSessionsDialog } from './RevokeAllSessionsDialog';
import { SessionsTable } from './SessionsTable';

export interface UserSessionsCardProps {
  user: Pick<User, 'id' | 'nombre'>;
  canUpdate: boolean;
}

/** "Sesiones activas": dispositivos con sesión abierta; revocar una o todas. */
export function UserSessionsCard({ user, canUpdate }: UserSessionsCardProps) {
  const toast = useToast();
  const { data, loading, error, reload, setData } = useAsync(() => usersService.listSessions(user.id), [user.id]);
  const sessions = data ?? [];
  const [target, setTarget] = useState<UserSession | null>(null);
  const [allOpen, setAllOpen] = useState(false);

  const revokeOne = async () => {
    if (!target) return;
    try {
      await usersService.revokeSession(user.id, target.id);
      setData((prev) => prev?.filter((s) => s.id !== target.id));
      toast({ title: 'Sesión revocada', description: `Se cerró la sesión en ${target.dispositivo}.`, tone: 'success' });
    } catch (e) {
      toast({ title: 'No se pudo revocar la sesión', description: errorMessage(e), tone: 'error' });
    } finally {
      setTarget(null);
    }
  };

  const revokeAll = async (force: boolean) => {
    try {
      const n = await usersService.revokeAllSessions(user.id, force);
      setData([]);
      setAllOpen(false);
      toast({
        title: 'Se cerraron todas sus sesiones.',
        description: `${n} ${n === 1 ? 'sesión cerrada' : 'sesiones cerradas'}.${force ? ' Además deberá cambiar la contraseña en su próximo acceso.' : ''}`,
        tone: 'success',
      });
    } catch (e) {
      toast({ title: 'No se pudieron cerrar las sesiones', description: errorMessage(e), tone: 'error' });
    }
  };

  return (
    <Card
      title={
        <span className="flex items-center gap-2">
          Sesiones activas{' '}
          <span className="tabular rounded-full bg-surface-hover px-2 text-xs font-semibold text-ink-3">{sessions.length}</span>
        </span>
      }
      subtitle="Dispositivos donde la cuenta tiene una sesión abierta."
      actions={
        canUpdate && sessions.length > 0 ? (
          <Button variant="secondary" size="sm" onClick={() => setAllOpen(true)}>
            Revocar todas
          </Button>
        ) : undefined
      }
    >
      {error ? (
        <ErrorState variant="banner" title="No se pudieron cargar las sesiones" message={error} onRetry={reload} />
      ) : (
        <SessionsTable
          sessions={sessions}
          loading={loading}
          canRevoke={canUpdate}
          onRevoke={setTarget}
          emptyState={
            <EmptyState
              icon={MonitorX}
              title="Sin sesiones activas"
              description="La cuenta tendrá que iniciar sesión de nuevo en cualquier dispositivo."
            />
          }
        />
      )}
      <ConfirmDialog
        open={!!target}
        title="¿Revocar esta sesión?"
        message={`Se cerrará la sesión en ${target?.dispositivo ?? ''} (${target?.ip ?? ''}). ${user.nombre} tendrá que iniciar sesión de nuevo en ese dispositivo.`}
        confirmLabel="Revocar sesión"
        onConfirm={revokeOne}
        onCancel={() => setTarget(null)}
      />
      <RevokeAllSessionsDialog
        open={allOpen}
        userName={user.nombre}
        count={sessions.length}
        onCancel={() => setAllOpen(false)}
        onConfirm={revokeAll}
      />
    </Card>
  );
}
