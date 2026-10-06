import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { AuthProvider, Permissions, resolveHomeRoute, type WebUser } from '../../../auth';
import { ToastProvider } from '../../../components/shared';
import { AppRoutes } from '../../../routes';
import { ADMIN_USER, NURSE_USER, createFakeBff, json, session } from '../../../test/fakeBff';

// Permisos reales de RolePermissions.Receptionist (backend).
const RECEPTIONIST: WebUser = {
  id: '44444444-4444-4444-4444-444444444444',
  userName: 'recepcion.prueba',
  email: null,
  roles: ['Receptionist'],
  permissions: ['Patients.Read', 'Patients.Create', 'Patients.Update', 'Patients.Identify', 'Accounts.InvitePatient', 'Encounters.Create', 'Transfers.Read', 'Transfers.Manage', 'Documents.Upload'],
};

// Administrator real: RolePermissions concede Permissions.All (administración + clínicos).
const ALL_PERMISSIONS = Object.values(Permissions).flatMap((group) => Object.values(group) as string[]);
const FULL_ADMIN: WebUser = { ...ADMIN_USER, permissions: ALL_PERMISSIONS };

function renderAppAs(user: WebUser, path: string) {
  createFakeBff({
    'POST /api/web/auth/refresh': () => json(200, session(user)),
    'GET /api/web/auth/me': () => json(200, user),
  });
  return render(
    <ToastProvider>
      <AuthProvider>
        <MemoryRouter initialEntries={[path]}>
          <AppRoutes />
        </MemoryRouter>
      </AuthProvider>
    </ToastProvider>,
  );
}

// Las páginas hospitalarias son lazy(): la primera importación en el test puede tardar más de 1 s.
const WAIT = { timeout: 5000 };
const sidebar = () => screen.getAllByRole('navigation', { name: 'Navegación principal' })[0];

describe('módulo Hospital con la autenticación compartida', () => {
  it('elige el módulo por permisos reales: Admin para administración, Hospital para personal clínico y recepción', () => {
    expect(resolveHomeRoute(ADMIN_USER.permissions)).toBe('/admin');
    expect(resolveHomeRoute(ALL_PERMISSIONS)).toBe('/admin');
    expect(resolveHomeRoute(RECEPTIONIST.permissions)).toBe('/hospital');
    expect(resolveHomeRoute(NURSE_USER.permissions)).toBe('/hospital');
    expect(resolveHomeRoute(['Accounts.InvitePatient'])).toBe('/admin');
    expect(resolveHomeRoute([])).toBe('/403');
  });

  it('una recepcionista que entra por / llega a /hospital/recepcion con el menú hospitalario', async () => {
    renderAppAs(RECEPTIONIST, '/');
    expect(await screen.findByRole('heading', { name: 'Recepción' }, WAIT)).toBeInTheDocument();
    const nav = within(sidebar());
    expect(nav.getByText('Transferencias')).toBeInTheDocument();
    expect(nav.queryByText('Usuarios')).not.toBeInTheDocument();
    // Sin Triage.Write ni Encounters.Read no ve esas entradas.
    expect(nav.queryByText('Triage')).not.toBeInTheDocument();
    expect(nav.queryByText('Atenciones')).not.toBeInTheDocument();
    // Tiene Accounts.InvitePatient (permiso de Admin): se le ofrece el cambio de módulo.
    expect(nav.getByText('Ir a Administración')).toBeInTheDocument();
  });

  it('sin el permiso de la pantalla hospitalaria recibe 403 y conserva la sesión', async () => {
    renderAppAs(RECEPTIONIST, '/hospital/triage');
    expect(await screen.findByText('ERROR 403', {}, WAIT)).toBeInTheDocument();
    expect(screen.getByText('Triage.Write')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cerrar sesión' })).toBeInTheDocument();
  });

  it('personal clínico sin permisos de Admin no ve el enlace a Administración', async () => {
    renderAppAs(NURSE_USER, '/hospital/triage');
    expect(await screen.findByRole('heading', { name: 'Triage' }, WAIT)).toBeInTheDocument();
    expect(within(sidebar()).queryByText('Ir a Administración')).not.toBeInTheDocument();
  });

  it('un administrador en /hospital ve la navegación hospitalaria (no la de Admin) y puede volver a Administración', async () => {
    renderAppAs(FULL_ADMIN, '/hospital/atenciones');
    expect(await screen.findByRole('heading', { name: 'Atenciones' }, WAIT)).toBeInTheDocument();
    const nav = within(sidebar());
    expect(nav.getByText('Recepción')).toBeInTheDocument();
    expect(nav.queryByText('Dispositivos')).not.toBeInTheDocument();
    expect(nav.getByText('Ir a Administración')).toBeInTheDocument();
  });

  it('una cuenta solo con permisos de Admin recibe 403 en el expediente hospitalario (sin cerrar sesión)', async () => {
    renderAppAs(ADMIN_USER, '/hospital/atenciones');
    expect(await screen.findByText('ERROR 403', {}, WAIT)).toBeInTheDocument();
    expect(screen.getByText('Encounters.Read')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cerrar sesión' })).toBeInTheDocument();
  });
});
