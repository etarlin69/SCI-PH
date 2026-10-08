import { AlertCircle, Lock } from 'lucide-react';
import type { AuditEvent } from '../../../types';
import { Button, CopyButton, DetailList, Modal } from '../../../components/shared';
import { formatAuditTimestamp } from '../auditFilters';
import { AuditActionTag } from './AuditActionTag';
import { AuditChangesTable } from './AuditChangesTable';
import { AuditResultBadge } from './AuditResultBadge';
import { roleLabel } from '../../../auth';

export interface AuditEventModalProps {
  /** Evento a mostrar; `null` cierra el modal. */
  event: AuditEvent | null;
  onClose: () => void;
  mobile?: boolean;
}

/** Detalle de un evento de auditoría (solo lectura): metadatos, user agent, cambios antes/después y hash de integridad con botón para copiarlo. */
export function AuditEventModal({ event, onClose, mobile = false }: AuditEventModalProps) {
  return (
    <Modal
      open={event !== null}
      onClose={onClose}
      size="lg"
      placement={mobile ? 'bottom' : 'center'}
      title={
        event ? (
          <span className="flex flex-col gap-1">
            <span className="font-mono text-xs font-normal text-ink-muted">{event.id}</span>
            <span className="break-words">
              {event.accion} · <span className="font-mono text-base">{event.recurso}</span>
            </span>
          </span>
        ) : (
          ''
        )
      }
      footer={
        event && (
          <>
            <div className="mr-auto flex min-w-0 flex-wrap items-center gap-2 text-xs text-ink-muted">
              <Lock size={14} aria-hidden="true" />
              <span>Hash de integridad</span>
              <span className="break-all font-mono text-ink-2">{event.hash}</span>
              <CopyButton value={event.hash} label="Copiar hash de integridad" className="h-8" />
            </div>
            <Button variant="secondary" onClick={onClose}>
              Cerrar
            </Button>
          </>
        )
      }
    >
      {event && (
        <div className="flex flex-col gap-5">
          <div className="flex flex-wrap items-center gap-2">
            <AuditActionTag action={event.accion} />
            <AuditResultBadge result={event.resultado} />
          </div>
          <p className="text-sm text-ink-2">{event.descripcion}</p>
          {event.resultado === 'Error' && event.error && (
            <div
              role="alert"
              className="flex items-start gap-2 rounded-lg border border-danger-600/30 bg-danger-50 px-3.5 py-2.5 text-[13px] text-danger-text"
            >
              <AlertCircle size={16} className="mt-px shrink-0" aria-hidden="true" />
              <span>
                <strong className="font-semibold">Motivo del error:</strong> {event.error}
              </span>
            </div>
          )}
          <DetailList
            items={[
              { label: 'Fecha / hora', value: `${formatAuditTimestamp(event.timestamp, true)} (hora local)`, mono: true },
              {
                label: 'Usuario',
                value: (
                  <>
                    <strong className="font-semibold">{event.usuario}</strong> · {event.email} · {roleLabel(event.rol)}
                  </>
                ),
              },
              { label: 'IP de origen', value: event.ip, mono: true },
              { label: 'ID del evento', value: event.id, mono: true },
            ]}
          />
          <DetailList columns={1} items={[{ label: 'User-Agent', value: event.userAgent || '—', mono: true }]} />
          <AuditChangesTable changes={event.cambios} />
        </div>
      )}
    </Modal>
  );
}
