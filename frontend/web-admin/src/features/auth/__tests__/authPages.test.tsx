import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { AuthProvider } from '../../../auth';
import { ToastProvider } from '../../../components/shared';
import { ADMIN_USER, createFakeBff, json, problem, session } from '../../../test/fakeBff';
import { LoginPage } from '../pages/LoginPage';
import { MfaPage, RECOVERY_CODE_PATTERN } from '../pages/MfaPage';
import { ResetPasswordPage } from '../pages/ResetPasswordPage';

function renderRoutes(path: string) {
  return render(
    <ToastProvider>
      <AuthProvider>
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/mfa" element={<MfaPage />} />
            <Route path="/reset-password" element={<ResetPasswordPage />} />
            <Route path="/admin/*" element={<p>Portal admin</p>} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </ToastProvider>,
  );
}

const noSession = () => problem(401, 'Authentication failed.');

async function fillLogin() {
  const user = userEvent.setup();
  await user.type(await screen.findByLabelText('Email o usuario'), 'admin.prueba');
  await user.type(screen.getByLabelText('Contraseña'), 'Secreta#2026x');
  await user.click(screen.getByRole('button', { name: 'Iniciar sesión' }));
  return user;
}

describe('LoginPage', () => {
  it('sin MFA entra directamente al módulo permitido', async () => {
    createFakeBff({
      'POST /api/web/auth/refresh': noSession,
      'POST /api/web/auth/login': () => json(200, { mfaRequired: false, session: session(ADMIN_USER), mfaChallenge: null }),
    });
    renderRoutes('/login');
    await fillLogin();
    expect(await screen.findByText('Portal admin')).toBeInTheDocument();
  });

  it('con MFA pasa a /mfa y un código de recuperación de Identity se envía como recoveryCode', async () => {
    const bff = createFakeBff({
      'POST /api/web/auth/refresh': noSession,
      'POST /api/web/auth/login': () =>
        json(200, { mfaRequired: true, session: null, mfaChallenge: { token: 'ch', expiresAtUtc: '2030-01-01T00:00:00Z' } }),
      'POST /api/web/auth/mfa/verify': () => json(200, session(ADMIN_USER)),
    });
    renderRoutes('/login');
    const user = await fillLogin();
    expect(await screen.findByText('Verificación en dos pasos')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Usar un código de recuperación' }));
    await user.type(screen.getByLabelText('Código de recuperación'), 'q7k2m-9xd4p');
    await user.click(screen.getByRole('button', { name: 'Verificar' }));

    expect(await screen.findByText('Portal admin')).toBeInTheDocument();
    expect(bff.calls('POST /api/web/auth/mfa/verify')[0].body).toEqual({ challengeToken: 'ch', recoveryCode: 'q7k2m-9xd4p' });
  });

  it('401 muestra un error genérico (sin revelar si la cuenta existe ni intentos restantes)', async () => {
    createFakeBff({ 'POST /api/web/auth/refresh': noSession, 'POST /api/web/auth/login': () => problem(401, 'Authentication failed.') });
    renderRoutes('/login');
    await fillLogin();
    expect(await screen.findByText('No se pudo iniciar sesión')).toBeInTheDocument();
    expect(screen.queryByText(/intentos/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/no existe/i)).not.toBeInTheDocument();
  });

  it('acepta el formato real de los códigos de recuperación y rechaza el inventado de 8', () => {
    expect(RECOVERY_CODE_PATTERN.test('q7k2m-9xd4p')).toBe(true);
    expect(RECOVERY_CODE_PATTERN.test('Q7K2M9XD4P')).toBe(true);
    expect(RECOVERY_CODE_PATTERN.test('7KQ2-M9XD')).toBe(false);
  });
});

describe('ResetPasswordPage', () => {
  it('envía userId + token del enlace del backend y la nueva contraseña', async () => {
    const bff = createFakeBff({ 'POST /api/web/auth/refresh': noSession, 'POST /api/web/auth/reset-password': () => new Response(null, { status: 204 }) });
    renderRoutes('/reset-password?userId=33333333-3333-3333-3333-333333333333&token=abc%2Bdef');
    const user = userEvent.setup();
    await user.type(await screen.findByLabelText('Nueva contraseña'), 'Nueva#Clave2026');
    await user.type(screen.getByLabelText('Confirmar contraseña'), 'Nueva#Clave2026');
    await user.click(screen.getByRole('button', { name: 'Guardar contraseña' }));

    expect(await screen.findByText('Contraseña actualizada')).toBeInTheDocument();
    expect(bff.calls('POST /api/web/auth/reset-password')[0].body).toEqual({
      userId: '33333333-3333-3333-3333-333333333333',
      token: 'abc+def',
      newPassword: 'Nueva#Clave2026',
    });
  });

  it('un enlace sin userId no llama al backend y ofrece pedir otro', async () => {
    const bff = createFakeBff({ 'POST /api/web/auth/refresh': noSession });
    renderRoutes('/reset-password?token=solo-token');
    expect(await screen.findByText('El enlace no es válido o caducó')).toBeInTheDocument();
    expect(bff.calls('POST /api/web/auth/reset-password')).toHaveLength(0);
  });

  it('un 400 de enlace inválido muestra el estado de enlace caducado', async () => {
    createFakeBff({
      'POST /api/web/auth/refresh': noSession,
      'POST /api/web/auth/reset-password': () => problem(400, 'The link is invalid or has expired.'),
    });
    renderRoutes('/reset-password?userId=u&token=t');
    const user = userEvent.setup();
    await user.type(await screen.findByLabelText('Nueva contraseña'), 'Nueva#Clave2026');
    await user.type(screen.getByLabelText('Confirmar contraseña'), 'Nueva#Clave2026');
    await user.click(screen.getByRole('button', { name: 'Guardar contraseña' }));
    expect(await screen.findByText('El enlace no es válido o caducó')).toBeInTheDocument();
  });
});
