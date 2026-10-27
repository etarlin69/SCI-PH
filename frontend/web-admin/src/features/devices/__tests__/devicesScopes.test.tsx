import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { AuthProvider } from '../../../auth';
import { ToastProvider } from '../../../components/shared';
import { AppRoutes } from '../../../routes';
import { ADMIN_USER, createFakeBff, json, session } from '../../../test/fakeBff';

const WAIT = { timeout: 5000 };

describe('dispositivos: permisos de huella', () => {
  it('ofrece el registro de huellas junto a la identificación y lo envía al registrar el lector', async () => {
    const bff = createFakeBff({
      'POST /api/web/auth/refresh': () => json(200, session(ADMIN_USER)),
      'GET /api/web/auth/me': () => json(200, ADMIN_USER),
      'GET /api/web/admin/devices': () => json(200, []),
      'POST /api/web/admin/devices': () => json(201, { id: 'd-1', deviceCode: 'ESP32-HOSP-01', secret: 'one-time-secret' }),
    });
    render(
      <ToastProvider>
        <AuthProvider>
          <MemoryRouter initialEntries={['/admin/dispositivos']}>
            <AppRoutes />
          </MemoryRouter>
        </AuthProvider>
      </ToastProvider>,
    );
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Registrar dispositivo' }, WAIT));
    const dialog = await screen.findByRole('dialog', {}, WAIT);
    expect(within(dialog).getByText('Device.Fingerprint.Enroll')).toBeInTheDocument();
    await user.type(within(dialog).getByRole('textbox', { name: /Código del dispositivo/ }), 'ESP32-HOSP-01');
    await user.type(within(dialog).getByRole('textbox', { name: /Nombre/ }), 'Recepción');
    await user.click(within(dialog).getByRole('checkbox', { name: /Identificación por huella/ }));
    await user.click(within(dialog).getByRole('checkbox', { name: /Registro de huellas/ }));
    await user.click(within(dialog).getByRole('button', { name: 'Registrar' }));

    await waitFor(() => expect(bff.calls('POST /api/web/admin/devices')).toHaveLength(1));
    const body = bff.calls('POST /api/web/admin/devices')[0].body as { scopes: string[] };
    expect(body.scopes.sort()).toEqual(['Device.Fingerprint.Enroll', 'Device.Fingerprint.Identify']);
  });
});
