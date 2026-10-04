import { Link } from 'react-router-dom';
import type { Invitation } from '../../../types';
import { Button, DetailList, Modal } from '../../../components/shared';
import { cn } from '../../../utils/cn';
import { canResend, canRevoke, expiryOf, expiryText, formatDateTime, parseDateTime } from '../helpers';
import { InvitationStatusBadge } from './InvitationStatusBadge';

export interface InvitationDetailDrawerProps {
  invitation: Invitation | null;
  canManage: boolean;
  acceptedUserId?: string;
  onClose: () => void;
  onResend: (inv: Invitation) => void;
  onRevoke: (inv: Invitation) => void;
}

/** Panel lateral con los datos, la caducidad y el historial de una invitación. */
export function InvitationDetailDrawer({
  invitation: inv,
  canManage,
  acceptedUserId,
  onClose,
  onResend,
  onRevoke,
}: InvitationDetailDrawerProps) {
  if (!inv) return null;
  const sent = formatDateTime(parseDateTime(inv.enviadaEl));
  const history: { t: string; m: string; tone: 'info' | 'success' | 'danger' | 'neutral' }[] = [
    { t: sent, m: `Enviada por ${inv.enviadaPor}`, tone: 'info' },
  ];
  if (inv.reenvios > 0)
    history.push({
      t: sent,
      m: `Reenviada ${inv.reenvios} ${inv.reenvios === 1 ? 'vez' : 'veces'} · cada reenvío invalida el enlace anterior`,
      tone: 'neutral',
    });
  if (inv.estado === 'Aceptada' && inv.aceptadaEl)
    history.push({ t: formatDateTime(parseDateTime(inv.aceptadaEl)), m: 'Aceptada · cuenta creada', tone: 'success' });
  if (inv.estado === 'Expirada') history.push({ t: formatDateTime(expiryOf(inv)), m: 'Caducó sin aceptarse', tone: 'neutral' });
  if (inv.estado === 'Revocada') history.push({ t: '—', m: 'Revocada · el enlace dejó de funcionar', tone: 'danger' });

  return (
    <Modal
      open
      onClose={onClose}
      placement="right"
      drawerWidth="md"
      title={inv.email}
      description={`${inv.id} · Invitación de ${inv.tipo.toLowerCase()}`}
      footer={
        <>
          {canManage && canRevoke(inv) && (
            <Button variant="danger" onClick={() => onRevoke(inv)}>
              Revocar
            </Button>
          )}
          {canManage && canResend(inv) && (
            <Button variant="secondary" onClick={() => onResend(inv)}>
              Reenviar invitación
            </Button>
          )}
          <Button variant="ghost" onClick={onClose}>
            Cerrar
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-6">
        <InvitationStatusBadge estado={inv.estado} />
        <DetailList
          items={[
            { label: 'Tipo', value: inv.tipo },
            ...(inv.tipo === 'Personal'
              ? [
                  { label: 'Rol', value: inv.rol ?? '—' },
                  { label: 'Departamento', value: inv.departamento ?? '—' },
                ]
              : []),
            { label: 'Enviada por', value: inv.enviadaPor },
            { label: 'Fecha de envío', value: sent },
            { label: inv.estado === 'Pendiente' ? 'Caduca' : 'Caducidad', value: expiryText(inv) },
            { label: 'Reenvíos', value: String(inv.reenvios) },
          ]}
        />
        {inv.mensaje && (
          <div className="flex flex-col gap-1">
            <span className="text-xs font-medium text-ink-muted">Mensaje personalizado</span>
            <p className="rounded-lg bg-surface-subtle px-3 py-2.5 text-sm text-ink-3">{inv.mensaje}</p>
          </div>
        )}
        <div className="flex flex-col gap-2">
          <h3 className="text-sm font-semibold text-ink">Historial</h3>
          <ol className="flex flex-col gap-3 border-l border-line pl-4">
            {history.map((h, i) => (
              <li key={i} className="relative flex flex-col">
                <span
                  aria-hidden="true"
                  className={cn(
                    'absolute -left-[21px] top-1.5 h-2.5 w-2.5 rounded-full',
                    h.tone === 'success'
                      ? 'bg-success-600'
                      : h.tone === 'danger'
                        ? 'bg-danger-600'
                        : h.tone === 'info'
                          ? 'bg-primary-600'
                          : 'bg-line-strong',
                  )}
                />
                <span className="tabular text-xs text-ink-muted">{h.t}</span>
                <span className="text-[13px] text-ink">{h.m}</span>
              </li>
            ))}
          </ol>
        </div>
        {inv.estado === 'Aceptada' && acceptedUserId && (
          <Link to={`/admin/usuarios/${acceptedUserId}`} className="text-[13px] font-semibold text-primary-600 hover:underline">
            Ver usuario
          </Link>
        )}
      </div>
    </Modal>
  );
}
