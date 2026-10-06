import { useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { ChevronLeft, ChevronRight, MailPlus, SearchX } from 'lucide-react';
import { Button, EmptyState, ErrorState, PageHeader, SearchInput, Table, type Column } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { useDebouncedValue } from '../../../hooks/useDebouncedValue';
import { formatUtcDateTime } from '../../../utils/dates';
import { MfaBadge, RoleList, UserStatusBadges } from '../components/UserBadges';
import { usersService } from '../services/usersService';
import type { UserSummary } from '../types/users.types';

const PAGE_SIZE = 20;

const COLUMNS: Column<UserSummary>[] = [
  {
    id: 'user',
    header: 'Cuenta',
    cell: (u) => (
      <span className="flex min-w-0 flex-col">
        <Link to={`/admin/usuarios/${u.id}`} className="truncate font-semibold text-ink hover:underline">
          {u.userName}
        </Link>
        {u.email && u.email !== u.userName && <span className="truncate text-xs text-ink-muted">{u.email}</span>}
      </span>
    ),
  },
  { id: 'roles', header: 'Roles', cell: (u) => <RoleList roles={u.roles} /> },
  { id: 'status', header: 'Estado', cell: (u) => <UserStatusBadges user={u} /> },
  { id: 'mfa', header: 'MFA', cell: (u) => <MfaBadge enabled={u.twoFactorEnabled} /> },
  { id: 'last', header: 'Último acceso', cell: (u) => <span className="tabular text-ink-3">{formatUtcDateTime(u.lastLoginAtUtc)}</span> },
];

/**
 * Cuentas de usuario (GET /api/web/admin/users, búsqueda y paginación en servidor). No hay alta ni borrado:
 * las cuentas se crean por invitación y se desactivan.
 */
export function UsersPage() {
  const navigate = useNavigate();
  const [search, setSearch] = useState('');
  const debounced = useDebouncedValue(search.trim(), 300);
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [debounced]);

  const q = useAsync(() => usersService.list({ search: debounced, page, pageSize: PAGE_SIZE }), [debounced, page]);
  const totalPages = q.data ? Math.max(1, Math.ceil(q.data.total / q.data.pageSize)) : 1;

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Usuarios"
        description="Cuentas con acceso a SCI-PH. Las cuentas nuevas se crean mediante invitaciones."
        actions={
          <Link
            to="/admin/invitaciones"
            className="inline-flex h-10 items-center gap-2 rounded-lg bg-primary-600 px-4 text-sm font-semibold text-white no-underline hover:bg-primary-700"
          >
            <MailPlus size={16} aria-hidden="true" />
            Invitar
          </Link>
        }
      />

      <div className="max-w-md">
        <SearchInput label="Buscar por usuario o email" value={search} onChange={setSearch} placeholder="Buscar por usuario o email" />
      </div>

      {q.error ? (
        <ErrorState message={q.error} onRetry={q.reload} />
      ) : (
        <>
          <Table
            caption="Cuentas de usuario"
            columns={COLUMNS}
            rows={q.data?.items ?? []}
            getRowId={(u) => u.id}
            loading={q.loading}
            onRowClick={(u) => navigate(`/admin/usuarios/${u.id}`)}
            emptyState={<EmptyState icon={SearchX} title={debounced ? 'Sin coincidencias' : 'No hay cuentas'} />}
            stickyFirstColumn
          />
          {q.data && q.data.total > q.data.pageSize && (
            <nav aria-label="Paginación" className="flex items-center justify-between gap-3 text-sm text-ink-3">
              <span className="tabular">
                Página {q.data.page} de {totalPages} · {q.data.total} cuentas
              </span>
              <span className="flex gap-2">
                <Button variant="secondary" size="sm" disabled={page <= 1 || q.loading} onClick={() => setPage((p) => p - 1)} leftIcon={<ChevronLeft size={16} aria-hidden="true" />}>
                  Anterior
                </Button>
                <Button variant="secondary" size="sm" disabled={page >= totalPages || q.loading} onClick={() => setPage((p) => p + 1)}>
                  Siguiente
                  <ChevronRight size={16} aria-hidden="true" />
                </Button>
              </span>
            </nav>
          )}
        </>
      )}
    </div>
  );
}

export default UsersPage;
