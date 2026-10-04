import { Link } from 'react-router-dom';
import type { StaffMember, User } from '../../../types';
import { Card, DetailList } from '../../../components/shared';
import { formatDMY, formatDateTimeText } from '../../../utils/dates';

const ACCOUNT_STATE: Record<User['estado'], string> = {
  Activo: 'activa',
  Pendiente: 'pendiente',
  Suspendido: 'suspendida',
  Inactivo: 'inactiva',
};

export interface StaffProfileCardProps {
  member: StaffMember;
  clinicName: string;
  account?: User | null;
  canViewUsers: boolean;
}

/** Tarjeta "Información" + "Cuenta de acceso" del perfil laboral. */
export function StaffProfileCard({ member: m, clinicName, account, canViewUsers }: StaffProfileCardProps) {
  let accountNode;
  if (m.cuenta.estado === 'vinculada') {
    accountNode = (
      <div className="flex flex-col gap-1">
        <span>
          <span className="font-mono">{m.cuenta.userId}</span>
          {account && ` · rol ${account.rol}`}
        </span>
        {account && (
          <span className="text-xs text-ink-muted">
            Cuenta {ACCOUNT_STATE[account.estado]} · MFA {account.mfaActivo ? 'activado' : 'pendiente'} · último acceso{' '}
            {formatDateTimeText(account.ultimoAcceso)}
          </span>
        )}
        {canViewUsers && (
          <Link to={`/admin/usuarios/${m.cuenta.userId}`} className="w-fit text-[13px] font-semibold text-primary-600 hover:underline">
            Ver usuario y sesiones
          </Link>
        )}
      </div>
    );
  } else if (m.cuenta.estado === 'pendiente') {
    accountNode = (
      <span>
        Invitación de acceso pendiente. Puedes reenviarla desde{' '}
        <Link to="/admin/invitaciones" className="font-semibold text-primary-600 hover:underline">
          Invitaciones
        </Link>
        .
      </span>
    );
  } else {
    accountNode = <span className="text-ink-muted">Sin acceso al sistema. Puedes invitarle desde Invitaciones.</span>;
  }

  return (
    <Card title="Información">
      <DetailList
        items={[
          { label: 'Email', value: m.email },
          { label: 'Teléfono', value: m.telefono ?? '—' },
          { label: 'Puesto', value: m.puesto },
          {
            label: 'Clínica',
            value: (
              <Link to={`/admin/clinicas/${m.clinicId}`} className="font-medium text-primary-600 hover:underline">
                {clinicName}
              </Link>
            ),
          },
          { label: 'Departamento', value: m.departamento },
          { label: 'Fecha de ingreso', value: formatDMY(m.fechaIngreso) },
          { label: 'Cuenta de acceso', value: accountNode },
        ]}
      />
    </Card>
  );
}
