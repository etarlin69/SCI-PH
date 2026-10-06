import { useState } from 'react';
import { LogOut } from 'lucide-react';
import { describeApiError } from '../../../api';
import { authApi, roleLabel, useAuth } from '../../../auth';
import { Avatar, Badge, Button, Card, ConfirmDialog, DetailList, PageHeader, useToast } from '../../../components/shared';
import { formatUtcDateTime } from '../../../utils/dates';
import { ChangePasswordCard } from '../components/ChangePasswordCard';
import { MfaCard } from '../components/MfaCard';

/** `/admin/perfil`: la cuenta en sesión (GET /me), contraseña, verificación en dos pasos y cierre de sesiones. */
export function ProfilePage() {
  const { user, permissions, accessTokenExpiresAtUtc, endSession } = useAuth();
  const toast = useToast();
  const [confirmRevoke, setConfirmRevoke] = useState(false);

  if (!user) return null;

  const revokeAll = async () => {
    try {
      await authApi.revokeOwnSessions();
      endSession('logout');
    } catch (e) {
      toast({ title: 'No se pudieron cerrar las sesiones', description: describeApiError(e), tone: 'error' });
    } finally {
      setConfirmRevoke(false);
    }
  };

  return (
    <div className="flex flex-col gap-5">
      <PageHeader title="Mi perfil" description="Tu cuenta, tu contraseña y la verificación en dos pasos." />
      <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_360px]">
        <div className="flex min-w-0 flex-col gap-5">
          <ChangePasswordCard />
          <MfaCard />
        </div>
        <div className="flex flex-col gap-5">
          <Card>
            <div className="flex items-center gap-3">
              <Avatar name={user.userName} size="lg" />
              <div className="flex min-w-0 flex-col">
                <span className="truncate text-[15px] font-semibold text-ink">{user.userName}</span>
                {user.email && <span className="truncate text-[13px] text-ink-muted">{user.email}</span>}
              </div>
            </div>
            <DetailList
              className="mt-4"
              columns={1}
              items={[
                { label: 'Id', value: user.id, mono: true },
                {
                  label: 'Roles',
                  value: (
                    <span className="flex flex-wrap gap-1">
                      {user.roles.length === 0
                        ? 'Sin rol'
                        : user.roles.map((r) => (
                            <Badge key={r} tone="info">
                              {roleLabel(r)}
                            </Badge>
                          ))}
                    </span>
                  ),
                },
                { label: 'Permisos', value: `${permissions.length}` },
                { label: 'Sesión válida hasta', value: formatUtcDateTime(accessTokenExpiresAtUtc) },
              ]}
            />
          </Card>
          <Card title="Sesiones">
            <p className="mb-3 text-[13px] text-ink-3">Cierra la sesión en todos los navegadores y dispositivos, incluido este.</p>
            <Button variant="secondary" leftIcon={<LogOut size={16} aria-hidden="true" />} onClick={() => setConfirmRevoke(true)}>
              Cerrar todas mis sesiones
            </Button>
          </Card>
        </div>
      </div>
      <ConfirmDialog
        open={confirmRevoke}
        title="Cerrar todas tus sesiones"
        message="Tendrás que volver a iniciar sesión en todos tus dispositivos."
        confirmLabel="Cerrar sesiones"
        onConfirm={revokeAll}
        onCancel={() => setConfirmRevoke(false)}
      />
    </div>
  );
}

export default ProfilePage;
