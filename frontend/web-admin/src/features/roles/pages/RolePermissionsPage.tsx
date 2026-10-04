import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Copy, Lock } from 'lucide-react';
import { useCan } from '../../../auth';
import { useBreadcrumb } from '../../../components/common';
import {
  Button,
  ConfirmDialog,
  ErrorState,
  FormError,
  LoadingSpinner,
  Modal,
  PageHeader,
  Select,
  useToast,
} from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { useSubmit } from '../../../hooks/useSubmit';
import { APPLICABLE_PERMISSIONS } from '../../../data/mock';
import { rolesService } from '../../../services';
import type { PermissionMatrix as Matrix, Role } from '../../../types';
import { PermissionChangesSummary } from '../components/PermissionChangesSummary';
import { PermissionMatrix } from '../components/PermissionMatrix';
import { RoleFormModal } from '../components/RoleFormModal';
import { RoleTypeBadge } from '../components/RoleTypeBadge';
import { UnsavedChangesBar } from '../components/UnsavedChangesBar';
import { countPermissions, diffPermissions } from '../permissions';

/** Matriz de permisos de un rol (`/admin/roles/:id/permisos`). */
export function RolePermissionsPage() {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const toast = useToast();
  const canUpdate = useCan('roles', 'update');
  const canCreate = useCan('roles', 'create');

  const rolesQ = useAsync(() => rolesService.list());
  const modulesQ = useAsync(() => rolesService.modules());
  const roles = useMemo(() => rolesQ.data ?? [], [rolesQ.data]);
  const role = roles.find((r) => r.id === id);
  useBreadcrumb(role ? `Permisos · ${role.nombre}` : 'Matriz de permisos');

  const [draft, setDraft] = useState<Matrix | null>(null);
  const [pendingRole, setPendingRole] = useState<string | null>(null);
  const [confirmSave, setConfirmSave] = useState(false);
  const [copyOpen, setCopyOpen] = useState(false);
  const save = useSubmit();

  useEffect(() => setDraft(role ? structuredClone(role.permisos) : null), [role]);

  const modules = modulesQ.data ?? [];
  const readOnly = !role || role.tipo === 'Sistema' || !canUpdate;
  const changes = role && draft ? diffPermissions(role.permisos, draft, modules) : [];
  const dirty = changes.length > 0;

  // Aviso del navegador al cerrar/recargar con cambios sin guardar.
  useEffect(() => {
    if (!dirty) return;
    const handler = (e: BeforeUnloadEvent) => e.preventDefault();
    window.addEventListener('beforeunload', handler);
    return () => window.removeEventListener('beforeunload', handler);
  }, [dirty]);

  const switchRole = (nextId: string) => {
    if (dirty) setPendingRole(nextId);
    else navigate(`/admin/roles/${nextId}/permisos`);
  };

  const doSave = async () => {
    if (!role || !draft) return;
    const updated = await save.run(() => rolesService.updatePermissions(role.id, draft));
    setConfirmSave(false);
    if (updated) {
      rolesQ.setData((prev) => prev?.map((r) => (r.id === updated.id ? updated : r)));
      toast({
        title: 'Permisos guardados',
        description: `Cambios en «${updated.nombre}». Se aplican en el próximo inicio de sesión.`,
        tone: 'success',
      });
    }
  };

  if (rolesQ.error || modulesQ.error) {
    return (
      <ErrorState
        title="No se pudo cargar la matriz"
        message={rolesQ.error ?? modulesQ.error ?? ''}
        onRetry={() => (rolesQ.reload(), modulesQ.reload())}
      />
    );
  }
  if (rolesQ.loading || modulesQ.loading || !draft) {
    if (!rolesQ.loading && !role) {
      return <ErrorState title="Rol no encontrado" message="El rol no existe o fue eliminado." onRetry={() => navigate('/admin/roles')} />;
    }
    return (
      <div className="flex justify-center py-24">
        <LoadingSpinner size="lg" label="Cargando permisos…" />
      </div>
    );
  }
  if (!role) return null;

  const grantsSensitive = changes.some((c) => c.granted && modules.find((m) => m.key === c.module)?.sensitive);

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Matriz de permisos"
        description="Qué puede hacer cada rol en cada módulo del portal."
        back={{ to: '/admin/roles', label: 'Roles y permisos' }}
      />

      <section
        className="flex flex-col gap-4 rounded-[10px] border border-line bg-white p-4 sm:flex-row sm:items-end sm:justify-between sm:p-5"
        aria-label="Rol seleccionado"
      >
        <div className="flex min-w-0 flex-col gap-3 sm:flex-row sm:items-end sm:gap-5">
          <Select
            label="Rol"
            value={role.id}
            onChange={(e) => switchRole(e.target.value)}
            options={roles.map((r) => ({ value: r.id, label: `${r.nombre} · ${r.tipo === 'Sistema' ? 'sistema' : 'personalizado'}` }))}
            containerClassName="sm:w-72"
          />
          <div className="flex flex-col gap-1">
            <span className="flex flex-wrap items-center gap-2">
              <span className="font-semibold text-ink">{role.nombre}</span>
              <RoleTypeBadge tipo={role.tipo} />
            </span>
            <span className="text-[13px] text-ink-muted">
              {role.descripcion} · <span className="tabular">{role.usuarios}</span> usuarios ·{' '}
              <span className="tabular">
                {countPermissions(draft)} de {APPLICABLE_PERMISSIONS}
              </span>{' '}
              permisos
            </span>
          </div>
        </div>
        {role.tipo === 'Sistema' && canCreate && (
          <Button variant="secondary" leftIcon={<Copy size={16} aria-hidden="true" />} onClick={() => setCopyOpen(true)}>
            Crear copia personalizada
          </Button>
        )}
      </section>

      <p className="flex items-center gap-2 text-[13px] text-ink-muted">
        {readOnly && <Lock size={14} aria-hidden="true" />}
        {readOnly
          ? 'Vista de solo lectura. «—» = no aplica (la auditoría nunca se modifica).'
          : 'Crear, Actualizar y Eliminar requieren Leer. «—» = no aplica (la auditoría nunca se modifica).'}
      </p>

      <PermissionMatrix
        modules={modules}
        value={draft}
        saved={role.permisos}
        onChange={setDraft}
        readOnly={readOnly}
        roleName={role.nombre}
      />

      {save.error && <FormError message={`No se pudo guardar. ${save.error}`} />}

      <UnsavedChangesBar
        count={changes.length}
        roleName={role.nombre}
        saving={save.submitting}
        onDiscard={() => setDraft(structuredClone(role.permisos))}
        onSave={() => setConfirmSave(true)}
      />

      <Modal
        open={confirmSave}
        onClose={() => setConfirmSave(false)}
        dismissible={!save.submitting}
        role="alertdialog"
        size="sm"
        title={`¿Guardar ${changes.length} ${changes.length === 1 ? 'cambio' : 'cambios'} en «${role.nombre}»?`}
        description={`Afecta a ${role.usuarios} usuario(s). Se aplican en el próximo inicio de sesión y queda registrado en auditoría.`}
        footer={
          <>
            <Button variant="secondary" onClick={() => setConfirmSave(false)} disabled={save.submitting}>
              Seguir editando
            </Button>
            <Button onClick={doSave} loading={save.submitting} loadingText="Guardando…">
              Guardar cambios
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3">
          {grantsSensitive && (
            <p className="rounded-lg bg-warning-50 px-3 py-2 text-[13px] text-warning-700">
              Estás concediendo permisos en módulos <strong>sensibles</strong> (gestión de cuentas o de permisos).
            </p>
          )}
          <PermissionChangesSummary changes={changes} modules={modules} />
        </div>
      </Modal>

      <ConfirmDialog
        open={!!pendingRole}
        title="¿Descartar los cambios sin guardar?"
        message={`Tienes ${changes.length} cambio(s) sin guardar en «${role.nombre}». Si cambias de rol se perderán.`}
        confirmLabel="Descartar y cambiar"
        onConfirm={() => {
          const next = pendingRole;
          setPendingRole(null);
          if (next) navigate(`/admin/roles/${next}/permisos`);
        }}
        onCancel={() => setPendingRole(null)}
      />

      <RoleFormModal
        open={copyOpen}
        roles={roles}
        copyFromId={role.id}
        onClose={() => setCopyOpen(false)}
        onSubmit={(input) => rolesService.create(input)}
        onSaved={(created: Role) => {
          setCopyOpen(false);
          rolesQ.setData((prev) => [...(prev ?? []), created]);
          toast({ title: 'Rol creado', description: `«${created.nombre}» copia los permisos de ${role.nombre}.`, tone: 'success' });
          navigate(`/admin/roles/${created.id}/permisos`);
        }}
      />
    </div>
  );
}

export default RolePermissionsPage;
