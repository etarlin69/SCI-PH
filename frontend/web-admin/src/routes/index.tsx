import { lazy, Suspense, type ComponentType, type ReactNode } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { RequireAuth, RequirePermission } from '../auth';
import { Layout } from '../components/common';
import { LoadingSpinner } from '../components/shared';
import ForbiddenPage from '../features/auth/pages/ForbiddenPage';
import NotFoundPage from '../features/auth/pages/NotFoundPage';
import type { ModuleKey } from '../types';
import { StatusShell } from './StatusShell';

/** Prefijo del área protegida. */
export const ADMIN_BASE = '/admin';

const page = (load: () => Promise<{ default: ComponentType }>) => lazy(load);

const LoginPage = page(() => import('../features/auth/pages/LoginPage'));
const MfaPage = page(() => import('../features/auth/pages/MfaPage'));
const ForgotPasswordPage = page(() => import('../features/auth/pages/ForgotPasswordPage'));
const ResetPasswordPage = page(() => import('../features/auth/pages/ResetPasswordPage'));
const ActivateAccountPage = page(() => import('../features/auth/pages/ActivateAccountPage'));
const ConfirmEmailPage = page(() => import('../features/auth/pages/ConfirmEmailPage'));
const DashboardPage = page(() => import('../features/dashboard/pages/DashboardPage'));
const UsersPage = page(() => import('../features/users/pages/UsersPage'));
const UserDetailPage = page(() => import('../features/users/pages/UserDetailPage'));
const RolesPage = page(() => import('../features/roles/pages/RolesPage'));
const RolePermissionsPage = page(() => import('../features/roles/pages/RolePermissionsPage'));
const InvitationsPage = page(() => import('../features/invitations/pages/InvitationsPage'));
const DevicesPage = page(() => import('../features/devices/pages/DevicesPage'));
const DeviceDetailPage = page(() => import('../features/devices/pages/DeviceDetailPage'));
const AuditPage = page(() => import('../features/audit/pages/AuditPage'));
const ClinicsPage = page(() => import('../features/clinics/pages/ClinicsPage'));
const ClinicDetailPage = page(() => import('../features/clinics/pages/ClinicDetailPage'));
const StaffPage = page(() => import('../features/staff/pages/StaffPage'));
const StaffDetailPage = page(() => import('../features/staff/pages/StaffDetailPage'));
const CatalogsPage = page(() => import('../features/catalogs/pages/CatalogsPage'));
const ProfilePage = page(() => import('../features/profile/pages/ProfilePage'));

function PageFallback() {
  return (
    <div className="flex justify-center py-24">
      <LoadingSpinner size="lg" label="Cargando…" />
    </div>
  );
}

const s = (node: ReactNode) => <Suspense fallback={<PageFallback />}>{node}</Suspense>;

const guarded = (module: ModuleKey, node: ReactNode) => <RequirePermission module={module}>{s(node)}</RequirePermission>;

/** Mapa de rutas del portal. */
export function AppRoutes() {
  return (
    <Routes>
      <Route index element={<Navigate to={`${ADMIN_BASE}/dashboard`} replace />} />

      {/* Públicas */}
      <Route path="login" element={s(<LoginPage />)} />
      <Route path="mfa" element={s(<MfaPage />)} />
      <Route path="forgot-password" element={s(<ForgotPasswordPage />)} />
      <Route path="reset-password" element={s(<ResetPasswordPage />)} />
      <Route path="activate-account" element={s(<ActivateAccountPage />)} />
      <Route path="confirm-email" element={s(<ConfirmEmailPage />)} />
      {/* Alias de las rutas en español del prototipo */}
      <Route path="recuperar" element={<Navigate to="/forgot-password" replace />} />
      <Route path="restablecer" element={<Navigate to="/reset-password?token=demo" replace />} />

      <Route element={<StatusShell />}>
        <Route path="403" element={<ForbiddenPage />} />
        <Route path="404" element={<NotFoundPage />} />
      </Route>

      {/* Protegidas */}
      <Route path="admin" element={<RequireAuth />}>
        <Route element={<Layout />}>
          <Route index element={<Navigate to="dashboard" replace />} />
          <Route path="dashboard" element={guarded('dashboard', <DashboardPage />)} />
          <Route path="usuarios" element={guarded('users', <UsersPage />)} />
          <Route path="usuarios/:id" element={guarded('users', <UserDetailPage />)} />
          <Route path="invitaciones" element={guarded('invitations', <InvitationsPage />)} />
          <Route path="roles" element={guarded('roles', <RolesPage />)} />
          <Route path="roles/:id/permisos" element={guarded('roles', <RolePermissionsPage />)} />
          <Route path="dispositivos" element={guarded('devices', <DevicesPage />)} />
          <Route path="dispositivos/:id" element={guarded('devices', <DeviceDetailPage />)} />
          <Route path="auditoria" element={guarded('audit', <AuditPage />)} />
          <Route path="clinicas" element={guarded('clinics', <ClinicsPage />)} />
          <Route path="clinicas/:id" element={guarded('clinics', <ClinicDetailPage />)} />
          <Route path="personal" element={guarded('staff', <StaffPage />)} />
          <Route path="personal/:id" element={guarded('staff', <StaffDetailPage />)} />
          <Route path="catalogos" element={guarded('catalogs', <CatalogsPage />)} />
          <Route path="catalogos/:tipo" element={guarded('catalogs', <CatalogsPage />)} />
          <Route path="perfil" element={s(<ProfilePage />)} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Route>

      <Route element={<StatusShell />}>
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  );
}

export default AppRoutes;
