import { describeApiError } from '../../../api';
import { roleLabel } from '../../../auth';
import { Badge } from '../../../components/shared';
import type { UserSummary } from '../types/users.types';

export const isLockedOut = (u: Pick<UserSummary, 'lockedOutUntilUtc'>, now = Date.now()) =>
  !!u.lockedOutUntilUtc && Date.parse(u.lockedOutUntilUtc) > now;

export function UserStatusBadges({ user }: { user: UserSummary }) {
  return (
    <span className="flex flex-wrap gap-1.5">
      <Badge tone={user.isActive ? 'success' : 'neutral'}>{user.isActive ? 'Activa' : 'Desactivada'}</Badge>
      {isLockedOut(user) && <Badge tone="danger">Bloqueada</Badge>}
      {!user.emailConfirmed && <Badge tone="warning">Email sin confirmar</Badge>}
    </span>
  );
}

export function MfaBadge({ enabled }: { enabled: boolean }) {
  return (
    <Badge tone={enabled ? 'info' : 'neutral'} dot={false}>
      {enabled ? 'MFA activo' : 'Sin MFA'}
    </Badge>
  );
}

export function RoleList({ roles }: { roles: readonly string[] }) {
  if (roles.length === 0) return <span className="text-ink-muted">Sin rol</span>;
  return (
    <span className="flex flex-wrap gap-1">
      {roles.map((r) => (
        <Badge key={r} tone="accent" dot={false}>
          {roleLabel(r)}
        </Badge>
      ))}
    </span>
  );
}

/** Mensajes de los resultados de UserAccountService (UsersController.ToResult). */
export const describeUserAdminError = (e: unknown) =>
  describeApiError(e, {
    400: 'Roles no válidos: solo roles predefinidos y Paciente no se combina con otros roles.',
    404: 'La cuenta no existe.',
    409: 'No es posible: el sistema debe conservar al menos un administrador activo y no puedes quitarte tu propio acceso de administrador.',
  });
