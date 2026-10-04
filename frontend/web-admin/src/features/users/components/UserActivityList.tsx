import { Link } from 'react-router-dom';
import { ArrowRight, History } from 'lucide-react';
import type { AuditEvent } from '../../../types';
import { usersService } from '../../../services';
import { Badge, Card, EmptyState, ErrorState, LoadingSpinner } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { cn } from '../../../utils/cn';
import { formatDateTimeShort } from '../format';

export interface UserActivityListProps {
  userId: string;
  email: string;
}

function dotClass(e: AuditEvent): string {
  if (e.resultado === 'Error') return 'bg-danger-600';
  if (e.accion === 'Inicio de sesión' || e.accion === 'Crear') return 'bg-success-600';
  if (e.accion === 'Revocar' || e.accion === 'Eliminar') return 'bg-warning-500';
  if (e.accion === 'Actualizar') return 'bg-primary-600';
  return 'bg-ink-muted';
}

/** Actividad reciente del usuario (eventos de Auditoría, solo lectura) con enlace a la Auditoría completa. */
export function UserActivityList({ userId, email }: UserActivityListProps) {
  const { data, loading, error, reload } = useAsync(() => usersService.listActivity(userId), [userId]);
  const auditLink = `/admin/auditoria?q=${encodeURIComponent(email)}`;

  return (
    <Card
      title="Actividad reciente"
      subtitle="Eventos de Auditoría de esta cuenta. El registro es inalterable: no se puede editar ni eliminar."
      actions={
        <Link
          to={auditLink}
          className="inline-flex shrink-0 items-center gap-1 rounded text-[13px] font-medium text-primary-600 hover:underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary-600"
        >
          Ver todo en Auditoría
          <ArrowRight size={14} aria-hidden="true" />
        </Link>
      }
    >
      {loading && !data ? (
        <div className="flex justify-center py-10">
          <LoadingSpinner label="Cargando actividad…" />
        </div>
      ) : error ? (
        <ErrorState title="No se pudo cargar la actividad" message={error} onRetry={reload} />
      ) : !data || data.length === 0 ? (
        <EmptyState icon={History} title="Sin actividad reciente" description="Todavía no hay eventos de auditoría para esta cuenta." />
      ) : (
        <ol className="flex flex-col" aria-label="Eventos recientes">
          {data.map((e) => (
            <li
              key={e.id}
              className="grid grid-cols-[auto_1fr] gap-x-3 border-b border-line-soft py-3 first:pt-0 last:border-b-0 last:pb-0 sm:grid-cols-[150px_auto_1fr]"
            >
              <time
                dateTime={e.timestamp.replace(' ', 'T')}
                className="col-span-2 font-mono text-xs text-ink-muted sm:col-span-1 sm:pt-0.5"
              >
                {formatDateTimeShort(e.timestamp)}
              </time>
              <span className="mt-1.5 flex h-2 w-2 shrink-0 rounded-full sm:mt-1.5" aria-hidden="true">
                <span className={cn('h-2 w-2 rounded-full', dotClass(e))} />
              </span>
              <div className="flex min-w-0 flex-col gap-1">
                <span className="text-sm text-ink">{e.descripcion}</span>
                <span className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-ink-muted">
                  <Badge tone={e.resultado === 'Error' ? 'danger' : 'neutral'} dot={false} className="px-2 py-0 text-[11px]">
                    {e.accion}
                  </Badge>
                  <span>{e.recurso}</span>
                  <span aria-hidden="true">·</span>
                  <span className="font-mono">{e.ip}</span>
                  {e.resultado === 'Error' && e.error && <span className="text-danger-text">· {e.error}</span>}
                </span>
              </div>
            </li>
          ))}
        </ol>
      )}
    </Card>
  );
}
