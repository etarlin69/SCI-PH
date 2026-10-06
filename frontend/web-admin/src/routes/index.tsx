import { lazy, Suspense, type ComponentType, type ReactNode } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { ADMIN_PERMISSIONS, Permissions, RequireAuth, RequirePermission, SessionLoading, resolveHomeRoute, useAuth, type Permission } from '../auth';
import { DEMO_ADMIN_PERMISSIONS, Layout, firstAdminRoute } from '../components/common';
import { LoadingSpinner } from '../components/shared';
import ForbiddenPage from '../features/auth/pages/ForbiddenPage';
import NotFoundPage from '../features/auth/pages/NotFoundPage';
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
const InvitationsPage = page(() => import('../features/invitations/pages/InvitationsPage'));
const DevicesPage = page(() => import('../features/devices/pages/DevicesPage'));
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

const guarded = (anyOf: readonly Permission[], node: ReactNode) => <RequirePermission anyOf={anyOf}>{s(node)}</RequirePermission>;

/** `/admin` → primera pantalla del portal que permiten los permisos del usuario. */
function AdminIndex() {
  const { canAny } = useAuth();
  return <Navigate to={firstAdminRoute(canAny)} replace />;
}

/** `/` → módulo que permiten los permisos (sin sesión, al login). */
function RootRedirect() {
  const { status, permissions } = useAuth();
  if (status === 'initializing') return <SessionLoading />;
  return <Navigate to={status === 'authenticated' ? resolveHomeRoute(permissions) : '/login'} replace />;
}

/** Mapa de rutas del portal. */
export function AppRoutes() {
  return (
    <Routes>
      <Route index element={<RootRedirect />} />

      {/* Públicas */}
      <Route path="login" element={s(<LoginPage />)} />
      <Route path="mfa" element={s(<MfaPage />)} />
      <Route path="forgot-password" element={s(<ForgotPasswordPage />)} />
      <Route path="reset-password" element={s(<ResetPasswordPage />)} />
      <Route path="activate-account" element={s(<ActivateAccountPage />)} />
      <Route path="confirm-email" element={s(<ConfirmEmailPage />)} />

      <Route element={<StatusShell />}>
        <Route path="403" element={<ForbiddenPage />} />
        <Route path="404" element={<NotFoundPage />} />
      </Route>

      {/* Protegidas */}
      <Route path="admin" element={<RequireAuth />}>
        <Route element={<Layout />}>
          <Route index element={<AdminIndex />} />
          <Route path="dashboard" element={guarded(ADMIN_PERMISSIONS, <DashboardPage />)} />
          <Route path="usuarios" element={guarded([Permissions.Users.Manage], <UsersPage />)} />
          <Route path="usuarios/:id" element={guarded([Permissions.Users.Manage], <UserDetailPage />)} />
          <Route
            path="invitaciones"
            element={guarded([Permissions.Users.Manage, Permissions.Accounts.InvitePatient], <InvitationsPage />)}
          />
          <Route path="roles" element={guarded([Permissions.Users.Manage], <RolesPage />)} />
          <Route path="dispositivos" element={guarded([Permissions.Devices.Manage], <DevicesPage />)} />
          <Route path="auditoria" element={guarded([Permissions.Audit.Read], <AuditPage />)} />
          <Route path="clinicas" element={guarded(DEMO_ADMIN_PERMISSIONS, <ClinicsPage />)} />
          <Route path="clinicas/:id" element={guarded(DEMO_ADMIN_PERMISSIONS, <ClinicDetailPage />)} />
          <Route path="personal" element={guarded(DEMO_ADMIN_PERMISSIONS, <StaffPage />)} />
          <Route path="personal/:id" element={guarded(DEMO_ADMIN_PERMISSIONS, <StaffDetailPage />)} />
          <Route path="catalogos" element={guarded(DEMO_ADMIN_PERMISSIONS, <CatalogsPage />)} />
          <Route path="catalogos/:tipo" element={guarded(DEMO_ADMIN_PERMISSIONS, <CatalogsPage />)} />
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
