import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { ADMIN_USER, NURSE_USER, createFakeBff, json, problem, session } from '../../test/fakeBff';
import { AuthProvider } from '../AuthContext';
import { isSafeInternalPath, postLoginRoute, resolveHomeRoute } from '../modules';
import { RequireAuth } from '../RequireAuth';
import { RequirePermission } from '../RequirePermission';

function renderAt(path: string, startupRetryDelaysMs?: number[]) {
  return render(
    <AuthProvider startupRetryDelaysMs={startupRetryDelaysMs}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/login" element={<p>Pantalla de login</p>} />
          <Route element={<RequireAuth />}>
            <Route path="/admin/usuarios" element={<RequirePermission permission="Users.Manage">{<p>Lista de usuarios</p>}</RequirePermission>} />
            <Route path="/admin/perfil" element={<p>Mi perfil</p>} />
          </Route>
        </Routes>
      </MemoryRouter>
    </AuthProvider>,
  );
}

describe('RequireAuth / RequirePermission', () => {
  it('mientras se recupera la sesión muestra la carga, no el contenido protegido', async () => {
    let release!: (r: Response) => void;
    createFakeBff({ 'POST /api/web/auth/refresh': () => new Promise<Response>((r) => (release = r)) });
    renderAt('/admin/perfil');
    expect(screen.getByText('Comprobando la sesión…')).toBeInTheDocument();
    expect(screen.queryByText('Mi perfil')).not.toBeInTheDocument();
    release(problem(401, 'Authentication failed.'));
    expect(await screen.findByText('Pantalla de login')).toBeInTheDocument();
  });

  it('si la sesión no se pudo comprobar (429/red) no redirige al login: ofrece reintentar y recupera la sesión', async () => {
    let limited = true;
    createFakeBff({
      'POST /api/web/auth/refresh': () => (limited ? problem(429, 'Too many requests.') : json(200, session(ADMIN_USER, 'ok'))),
      'GET /api/web/auth/me': () => json(200, ADMIN_USER),
    });
    renderAt('/admin/perfil', []);
    expect(await screen.findByText('No pudimos comprobar tu sesión')).toBeInTheDocument();
    expect(screen.queryByText('Pantalla de login')).not.toBeInTheDocument();
    expect(screen.queryByText('Mi perfil')).not.toBeInTheDocument();
    limited = false;
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    expect(await screen.findByText('Mi perfil')).toBeInTheDocument();
  });

  it('sin sesión redirige al login aunque se entre por URL directa', async () => {
    createFakeBff({ 'POST /api/web/auth/refresh': () => problem(401, 'Authentication failed.') });
    renderAt('/admin/usuarios');
    expect(await screen.findByText('Pantalla de login')).toBeInTheDocument();
    expect(screen.queryByText('Lista de usuarios')).not.toBeInTheDocument();
  });

  it('con sesión y el permiso real muestra la ruta', async () => {
    createFakeBff({
      'POST /api/web/auth/refresh': () => json(200, session(ADMIN_USER)),
      'GET /api/web/auth/me': () => json(200, ADMIN_USER),
    });
    renderAt('/admin/usuarios');
    expect(await screen.findByText('Lista de usuarios')).toBeInTheDocument();
  });

  it('con sesión pero sin el permiso muestra 403 y mantiene la sesión', async () => {
    createFakeBff({
      'POST /api/web/auth/refresh': () => json(200, session(NURSE_USER)),
      'GET /api/web/auth/me': () => json(200, NURSE_USER),
    });
    renderAt('/admin/usuarios');
    expect(await screen.findByText('ERROR 403')).toBeInTheDocument();
    expect(screen.getByText('Users.Manage')).toBeInTheDocument();
    expect(screen.queryByText('Lista de usuarios')).not.toBeInTheDocument();
    expect(screen.queryByText('Pantalla de login')).not.toBeInTheDocument();
  });
});

describe('resolución del destino tras el login', () => {
  it('elige el módulo por permisos reales y no por rol', () => {
    expect(resolveHomeRoute(ADMIN_USER.permissions)).toBe('/admin');
    expect(resolveHomeRoute(['Accounts.InvitePatient'])).toBe('/admin');
    // Con el módulo hospitalario registrado, los permisos clínicos llevan a /hospital.
    expect(resolveHomeRoute(NURSE_USER.permissions)).toBe('/hospital');
    expect(resolveHomeRoute([])).toBe('/403');
  });

  it('solo acepta rutas internas como retorno (sin open redirect)', () => {
    expect(isSafeInternalPath('/admin/usuarios?x=1')).toBe(true);
    expect(isSafeInternalPath('https://evil.example')).toBe(false);
    expect(isSafeInternalPath('//evil.example')).toBe(false);
    expect(isSafeInternalPath('/\\evil.example')).toBe(false);
    expect(postLoginRoute(ADMIN_USER.permissions, 'https://evil.example')).toBe('/admin');
    expect(postLoginRoute(ADMIN_USER.permissions, '/admin/dispositivos')).toBe('/admin/dispositivos');
    expect(postLoginRoute(ADMIN_USER.permissions, '/login')).toBe('/admin');
  });
});
