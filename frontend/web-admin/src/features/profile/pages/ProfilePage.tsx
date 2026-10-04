import { ShieldCheck, ShieldAlert } from 'lucide-react';
import { useAuth } from '../../auth/hooks/useAuth';
import { Avatar, Badge, Card, DetailList, ErrorState, LoadingSpinner, PageHeader, useToast } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { formatDMY, formatRelative } from '../../../utils/dates';
import { UserSessionsCard } from '../../users/components/UserSessionsCard';
import { ChangePasswordCard } from '../components/ChangePasswordCard';
import { ProfileForm } from '../components/ProfileForm';
import { profileService } from '../services/profileService';

/** `/admin/perfil`: datos propios, preferencias, contraseña, MFA y sesiones abiertas. */
export function ProfilePage() {
  const { user } = useAuth();
  const toast = useToast();
  const userId = user?.id ?? '';
  const { data: profile, loading, error, reload, setData } = useAsync(() => profileService.get(userId), [userId]);

  return (
    <div className="flex flex-col gap-5">
      <PageHeader title="Mi perfil" description="Gestiona tus datos, tu contraseña y las sesiones abiertas con tu cuenta." />

      {loading && !profile ? (
        <div className="flex justify-center py-20">
          <LoadingSpinner size="lg" label="Cargando tu perfil…" />
        </div>
      ) : error && !profile ? (
        <Card>
          <ErrorState title="No se pudo cargar tu perfil" message={error} onRetry={reload} />
        </Card>
      ) : profile ? (
        <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_360px]">
          <div className="flex min-w-0 flex-col gap-5">
            <ProfileForm
              profile={profile}
              onSave={(input) => profileService.update(userId, input)}
              onSaved={(p) => {
                setData(p);
                toast({ title: 'Perfil actualizado', tone: 'success' });
              }}
            />
            <ChangePasswordCard onChange={profileService.changePassword} />
            <UserSessionsCard user={{ id: profile.id, nombre: profile.nombre }} canUpdate />
          </div>
          <div className="flex flex-col gap-5">
            <Card>
              <div className="flex items-center gap-3">
                <Avatar name={profile.nombre} size="lg" />
                <div className="flex min-w-0 flex-col">
                  <span className="truncate text-[15px] font-semibold text-ink">{profile.nombre}</span>
                  <span className="truncate text-[13px] text-ink-muted">{profile.email}</span>
                </div>
              </div>
              <DetailList
                className="mt-4"
                columns={1}
                items={[
                  { label: 'ID', value: profile.id, mono: true },
                  { label: 'Rol', value: <Badge tone="info">{profile.rol}</Badge> },
                  { label: 'Último acceso', value: formatRelative(profile.ultimoAcceso) },
                  { label: 'Cuenta creada', value: formatDMY(profile.creadoEl) },
                ]}
              />
            </Card>
            <Card title="Verificación en dos pasos">
              <div className="flex items-start gap-3 text-[13px] text-ink-2">
                {profile.mfaActivo ? (
                  <ShieldCheck size={20} className="shrink-0 text-success-600" aria-hidden="true" />
                ) : (
                  <ShieldAlert size={20} className="shrink-0 text-warning-700" aria-hidden="true" />
                )}
                <p>
                  {profile.mfaActivo
                    ? 'Activa con aplicación de autenticación (TOTP). Si cambias de teléfono, pide a un administrador que restablezca tu MFA.'
                    : 'No configurada. Es obligatoria: se te pedirá en el próximo inicio de sesión.'}
                </p>
              </div>
            </Card>
          </div>
        </div>
      ) : null}
    </div>
  );
}

export default ProfilePage;
