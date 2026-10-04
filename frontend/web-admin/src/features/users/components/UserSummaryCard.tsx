import { Link } from 'react-router-dom';
import { ShieldCheck, ShieldOff } from 'lucide-react';
import type { User } from '../../../types';
import { Card, DetailList } from '../../../components/shared';
import { formatDMY } from '../../../utils/dates';
import { formatLastAccess, formatRelative } from '../format';
import { UserStatusBadge } from './UserStatusBadge';

export interface UserSummaryCardProps {
  user: User;
  canViewStaff?: boolean;
}

/** Ficha "Información de la cuenta": datos básicos, alta, último acceso, MFA y perfil de Personal vinculado. */
export function UserSummaryCard({ user, canViewStaff = true }: UserSummaryCardProps) {
  return (
    <Card title="Información de la cuenta">
      <DetailList
        items={[
          { label: 'Nombre', value: user.nombre },
          { label: 'Email', value: user.email },
          { label: 'Rol', value: user.rol },
          { label: 'Estado', value: <UserStatusBadge estado={user.estado} /> },
          { label: 'Id de usuario', value: user.id, mono: true },
          {
            label: 'Alta',
            value: (
              <span className="tabular">
                {formatDMY(user.creadoEl)} · por {user.creadoPor}
              </span>
            ),
          },
          {
            label: 'Último acceso',
            value: user.ultimoAcceso ? (
              <span className="tabular">
                {formatLastAccess(user.ultimoAcceso)} <span className="text-ink-muted">· {formatRelative(user.ultimoAcceso)}</span>
              </span>
            ) : (
              <span className="text-warning-700">Nunca ha iniciado sesión</span>
            ),
          },
          {
            label: 'MFA',
            value: user.mfaActivo ? (
              <span className="inline-flex items-center gap-1.5 text-success-600">
                <ShieldCheck size={15} aria-hidden="true" />
                Activado
              </span>
            ) : (
              <span className="inline-flex items-center gap-1.5 text-warning-700">
                <ShieldOff size={15} aria-hidden="true" />
                Pendiente de configurar
              </span>
            ),
          },
          {
            label: 'Perfil de personal',
            value: user.staffId ? (
              canViewStaff ? (
                <Link
                  to={`/admin/personal/${user.staffId}`}
                  className="rounded font-medium text-primary-600 hover:underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary-600"
                >
                  Ver perfil {user.staffId}
                </Link>
              ) : (
                <span className="font-mono text-[13px]">{user.staffId}</span>
              )
            ) : (
              <span className="text-ink-muted">Sin perfil laboral vinculado</span>
            ),
          },
        ]}
      />
    </Card>
  );
}
