import { Lock } from 'lucide-react';
import { roleLabel } from '../../../auth';
import { Badge, Card, EmptyState, ErrorState, LoadingSpinner, PageHeader } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { rolesService } from '../services/rolesService';

/** Agrupa `Area.Accion` por área para leer la matriz con facilidad. */
function groupPermissions(permissions: readonly string[]): [string, string[]][] {
  const groups = new Map<string, string[]>();
  for (const p of permissions) {
    const [area, action = p] = p.split('.');
    groups.set(area, [...(groups.get(area) ?? []), action]);
  }
  return [...groups.entries()].sort(([a], [b]) => a.localeCompare(b));
}

/** Roles predefinidos y sus permisos (GET /api/web/admin/roles). Solo lectura. */
export function RolesPage() {
  const q = useAsync(() => rolesService.list());

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Roles y permisos"
        description="Roles predefinidos de SCI-PH y los permisos que concede cada uno. Para cambiar el acceso de una persona, asígnale otros roles desde Usuarios."
        meta={
          <Badge tone="neutral" dot={false}>
            <Lock size={12} aria-hidden="true" className="mr-1 inline" />
            Solo lectura
          </Badge>
        }
      />

      {q.loading && !q.data ? (
        <div className="flex justify-center py-16">
          <LoadingSpinner label="Cargando roles…" />
        </div>
      ) : q.error ? (
        <ErrorState message={q.error} onRetry={q.reload} />
      ) : !q.data || q.data.length === 0 ? (
        <EmptyState title="No hay roles definidos" />
      ) : (
        <div className="grid gap-4 lg:grid-cols-2">
          {q.data.map((role) => (
            <Card key={role.name} title={roleLabel(role.name)} subtitle={<code className="font-mono text-xs">{role.name}</code>}>
              {role.permissions.length === 0 ? (
                <p className="text-sm text-ink-muted">Sin permisos genéricos. El acceso a su propio registro se comprueba por recurso.</p>
              ) : (
                <dl className="flex flex-col gap-2.5">
                  {groupPermissions(role.permissions).map(([area, actions]) => (
                    <div key={area} className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
                      <dt className="w-44 shrink-0 text-[13px] font-semibold text-ink-2">{area}</dt>
                      <dd className="flex flex-wrap gap-1">
                        {actions.map((a) => (
                          <Badge key={a} tone="info" dot={false}>
                            {a}
                          </Badge>
                        ))}
                      </dd>
                    </div>
                  ))}
                </dl>
              )}
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}

export default RolesPage;
