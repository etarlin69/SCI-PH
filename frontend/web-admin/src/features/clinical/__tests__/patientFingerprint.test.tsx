import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { AuthProvider, type WebUser } from '../../../auth';
import { ToastProvider } from '../../../components/shared';
import { AppRoutes } from '../../../routes';
import { createFakeBff, json, problem, session, type RecordedRequest } from '../../../test/fakeBff';
import { resetClinicSelectionForTests } from '../api/clinicSelection';
import type { FingerprintEnrollment, PatientFingerprint, PatientSummary } from '../types/clinical.types';

// Permisos reales de RolePermissions (backend).
const RECEPTIONIST: WebUser = {
  id: '44444444-4444-4444-4444-444444444444',
  userName: 'recepcion',
  email: null,
  roles: ['Receptionist'],
  permissions: ['Patients.Read', 'Patients.Create', 'Patients.Update', 'Patients.Identify', 'Biometrics.Manage', 'Accounts.InvitePatient', 'Encounters.Create', 'Transfers.Read', 'Transfers.Manage', 'Documents.Upload'],
};
const DOCTOR: WebUser = {
  id: '55555555-5555-5555-5555-555555555555',
  userName: 'medico',
  email: null,
  roles: ['Doctor'],
  permissions: ['Patients.Read', 'Encounters.Read', 'Medications.Read', 'Transfers.Read', 'Documents.Read', 'Patients.Identify'],
};

const WAIT = { timeout: 5000 };
const PATIENT: PatientSummary = {
  id: 'p-1',
  displayName: 'Ana Pérez',
  birthDate: '1985-03-14',
  biologicalSex: 'Female',
  identityStatus: 'Verified',
  primaryIdentifier: { type: 'NationalId', value: '***5678' },
};
const READER = { id: 'reader-1', code: 'ESP32-HOSP-01', name: 'Recepción', lastSeenAtUtc: null };
const enrollment = (status: FingerprintEnrollment['status'], failure: FingerprintEnrollment['failure'] = null): FingerprintEnrollment => ({
  id: 'enr-1',
  status,
  readerId: READER.id,
  expiresAtUtc: '2030-01-01T00:02:00Z',
  failure,
  biometricReferenceId: status === 'Enrolled' ? 'ref-1' : null,
});

// Backend con estado: el lector responde lo guionado en las consultas sucesivas del registro.
function backend(answers: FingerprintEnrollment[]) {
  const fingerprints: PatientFingerprint[] = [];
  const routes: Record<string, (req: RecordedRequest, n: number) => Response> = {
    'GET /api/web/clinical/context': () => json(200, { staffId: 's-1', clinics: [{ id: 'clinic-1', name: 'Hospital' }] }),
    'GET /api/web/patients/p-1': () => json(200, PATIENT),
    'POST /api/web/patients/search': () => json(200, [PATIENT]),
    'GET /api/web/patients/p-1/fingerprints': () => json(200, fingerprints),
    'GET /api/web/fingerprint-enrollers': () => json(200, [READER]),
    'POST /api/web/patients/p-1/fingerprints/enrollments': () => json(201, enrollment('Pending')),
    'GET /api/web/patients/p-1/fingerprints/enrollments/enr-1': (_req, n) => {
      const answer = answers[Math.min(n, answers.length - 1)];
      if (answer.status === 'Enrolled' && fingerprints.length === 0) {
        fingerprints.push({ id: 'ref-1', status: 'Active', reader: { code: READER.code, name: READER.name, available: true }, registeredAtUtc: '2030-01-01T00:01:00Z', revokedAtUtc: null, cleanup: null });
      }
      return json(200, answer);
    },
    'POST /api/web/patients/p-1/fingerprints/enrollments/enr-1/cancel': () => json(200, enrollment('Cancelled')),
  };
  return { routes, fingerprints };
}

function renderAs(user: WebUser, routes: Record<string, (req: RecordedRequest, n: number) => Response>, path = '/hospital/pacientes/p-1') {
  resetClinicSelectionForTests();
  const bff = createFakeBff({
    'POST /api/web/auth/refresh': () => json(200, session(user)),
    'GET /api/web/auth/me': () => json(200, user),
    ...routes,
  });
  const view = render(
    <ToastProvider>
      <AuthProvider>
        <MemoryRouter initialEntries={[path]}>
          <AppRoutes />
        </MemoryRouter>
      </AuthProvider>
    </ToastProvider>,
  );
  return { bff, view };
}

const card = async () => (await screen.findByRole('heading', { name: 'Huella digital' }, WAIT)).closest('section')!;

async function startEnrollment() {
  const user = userEvent.setup();
  const fingerprint = await card();
  await user.click(await within(fingerprint).findByRole('button', { name: /Registrar huella/ }, WAIT));
  const dialog = await screen.findByRole('alertdialog', {}, WAIT);
  expect(within(dialog).getByText(/Confirme que desea registrar la huella de este paciente/)).toBeInTheDocument();
  await user.click(within(dialog).getByRole('button', { name: 'Registrar' }));
  return { user, fingerprint };
}

describe('detalle del paciente y huella digital', () => {
  it('abre el detalle desde la búsqueda y muestra los datos del paciente', async () => {
    const user = userEvent.setup();
    renderAs(RECEPTIONIST, backend([enrollment('Pending')]).routes, '/hospital/pacientes');

    await user.type(await screen.findByRole('textbox', { name: /Valor/ }, WAIT), '00112345678');
    await user.click(screen.getByRole('button', { name: 'Buscar' }));
    await user.click(await screen.findByRole('link', { name: 'Abrir Ana Pérez' }, WAIT));

    expect(await screen.findByRole('heading', { name: 'Paciente' }, WAIT)).toBeInTheDocument();
    expect(await screen.findByText('Ana Pérez', {}, WAIT)).toBeInTheDocument();
    expect(await within(await card()).findByText('Sin huella registrada', {}, WAIT)).toBeInTheDocument();
  });

  it('registra la huella: confirma, abre el registro solo con clave y lector, consulta y muestra la huella', async () => {
    const { routes } = backend([enrollment('Pending'), enrollment('Enrolled')]);
    const { bff } = renderAs(RECEPTIONIST, routes);
    const { fingerprint } = await startEnrollment();

    await waitFor(() => expect(bff.calls('POST /api/web/patients/p-1/fingerprints/enrollments')).toHaveLength(1));
    const body = bff.calls('POST /api/web/patients/p-1/fingerprints/enrollments')[0].body as Record<string, string>;
    expect(Object.keys(body).sort()).toEqual(['clientRequestId', 'deviceId']);
    expect(body.deviceId).toBe(READER.id);
    expect(await within(fingerprint).findByText(/Esperando al lector/, {}, WAIT)).toBeInTheDocument();

    expect(await within(fingerprint).findByText(/Huella registrada\. Ya puede usarse/, {}, WAIT)).toBeInTheDocument();
    expect(await within(fingerprint).findByText('Activa', {}, WAIT)).toBeInTheDocument();
    expect(within(within(fingerprint).getByRole('list', { name: 'Huellas registradas' })).getByText(`${READER.name} (${READER.code})`)).toBeInTheDocument();
    expect(fingerprint.textContent).not.toMatch(/slot|r307|subjectReference/i);
    // La consulta terminó al completarse el registro.
    const polls = bff.calls('GET /api/web/patients/p-1/fingerprints/enrollments/enr-1').length;
    await new Promise((resolve) => setTimeout(resolve, 1300));
    expect(bff.calls('GET /api/web/patients/p-1/fingerprints/enrollments/enr-1')).toHaveLength(polls);
  });

  it.each([
    ['LowQuality', enrollment('Failed', 'LowQuality'), /lectura de la huella no fue suficiente/i],
    ['SensorError', enrollment('Failed', 'SensorError'), /lector no está disponible/i],
    ['Expired', enrollment('Expired'), /venció sin respuesta del lector/i],
  ])('un registro que termina sin huella (%s) lo dice y el siguiente intento usa otra clave', async (_name, answer, message) => {
    const { bff } = renderAs(RECEPTIONIST, backend([answer]).routes);
    const { user, fingerprint } = await startEnrollment();

    expect(await within(fingerprint).findByText(message, {}, WAIT)).toBeInTheDocument();
    await user.click(within(fingerprint).getByRole('button', { name: /Registrar huella/ }));
    await user.click(within(await screen.findByRole('alertdialog', {}, WAIT)).getByRole('button', { name: 'Registrar' }));

    await waitFor(() => expect(bff.calls('POST /api/web/patients/p-1/fingerprints/enrollments')).toHaveLength(2));
    const [first, second] = bff.calls('POST /api/web/patients/p-1/fingerprints/enrollments').map((r) => (r.body as { clientRequestId: string }).clientRequestId);
    expect(first).not.toBe(second);
  });

  it('cancela un registro en curso', async () => {
    const { bff } = renderAs(RECEPTIONIST, backend([enrollment('Pending')]).routes);
    const { user, fingerprint } = await startEnrollment();

    await user.click(await within(fingerprint).findByRole('button', { name: 'Cancelar registro' }, WAIT));

    expect(await within(fingerprint).findByText('Registro cancelado.', {}, WAIT)).toBeInTheDocument();
    expect(bff.calls('POST /api/web/patients/p-1/fingerprints/enrollments/enr-1/cancel')).toHaveLength(1);
    const polls = bff.calls('GET /api/web/patients/p-1/fingerprints/enrollments/enr-1').length;
    await new Promise((resolve) => setTimeout(resolve, 1300));
    expect(bff.calls('GET /api/web/patients/p-1/fingerprints/enrollments/enr-1')).toHaveLength(polls);
  });

  it('muestra los errores del BFF con mensajes seguros', async () => {
    const { routes } = backend([enrollment('Pending')]);
    renderAs(RECEPTIONIST, {
      ...routes,
      'POST /api/web/patients/p-1/fingerprints/enrollments': () =>
        problem(409, 'The request conflicts with the current state.', { detail: 'The patient already has an active fingerprint on this reader.' }),
    });
    const { fingerprint } = await startEnrollment();

    expect(await within(fingerprint).findByText('El paciente ya tiene una huella activa en este lector.', {}, WAIT)).toBeInTheDocument();
  });

  it('deja de consultar al salir de la pantalla', async () => {
    const { bff, view } = renderAs(RECEPTIONIST, backend([enrollment('Pending')]).routes);
    const { fingerprint } = await startEnrollment();
    await within(fingerprint).findByText(/Esperando al lector/, {}, WAIT);

    view.unmount();
    const polls = bff.calls('GET /api/web/patients/p-1/fingerprints/enrollments/enr-1').length;
    await new Promise((resolve) => setTimeout(resolve, 1300));

    expect(bff.calls('GET /api/web/patients/p-1/fingerprints/enrollments/enr-1')).toHaveLength(polls);
  });

  it('sin Biometrics.Manage se ve el paciente pero no la huella', async () => {
    const { bff } = renderAs(DOCTOR, backend([enrollment('Pending')]).routes);

    expect(await screen.findByText('Ana Pérez', {}, WAIT)).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Huella digital' })).not.toBeInTheDocument();
    expect(bff.calls('GET /api/web/fingerprint-enrollers')).toHaveLength(0);
    expect(bff.calls('GET /api/web/patients/p-1/fingerprints')).toHaveLength(0);
  });
});

const ACTIVE: PatientFingerprint = {
  id: 'ref-1',
  status: 'Active',
  reader: { code: READER.code, name: READER.name, available: true },
  registeredAtUtc: '2030-01-01T00:01:00Z',
  revokedAtUtc: null,
  cleanup: null,
};
const revoked = (cleanup: PatientFingerprint['cleanup']): PatientFingerprint => ({ ...ACTIVE, status: 'Revoked', revokedAtUtc: '2030-01-02T00:00:00Z', cleanup });
const REVOKE = 'POST /api/web/patients/p-1/fingerprints/ref-1/revoke';

// Backend con una huella activa; la revocación responde lo indicado y la lista posterior refleja el estado guardado.
function withActive(revokeAnswer: () => Response, afterRevoke: PatientFingerprint = revoked('Pending')) {
  let current: PatientFingerprint = ACTIVE;
  const routes: Record<string, (req: RecordedRequest, n: number) => Response> = {
    ...backend([enrollment('Pending')]).routes,
    'GET /api/web/patients/p-1/fingerprints': () => json(200, [current]),
    [REVOKE]: () => {
      const response = revokeAnswer();
      if (response.ok) current = afterRevoke;
      return response;
    },
  };
  return routes;
}

async function confirmRevoke(reason?: string) {
  const user = userEvent.setup();
  const fingerprint = await card();
  await user.click(await within(fingerprint).findByRole('button', { name: 'Revocar huella' }, WAIT));
  const dialog = await screen.findByRole('alertdialog', {}, WAIT);
  expect(within(dialog).getByText(/dejará de servir para identificar de inmediato\. El lector borrará su copia cuando se conecte/)).toBeInTheDocument();
  if (reason) await user.selectOptions(within(dialog).getByRole('combobox', { name: /Motivo/ }), reason);
  await user.click(within(dialog).getByRole('button', { name: 'Revocar' }));
  return { user, fingerprint };
}

describe('revocación de la huella', () => {
  it('una huella activa se revoca tras confirmar, con el motivo opcional como único dato enviado, y se ve Revocada al instante', async () => {
    const { bff } = renderAs(RECEPTIONIST, withActive(() => json(200, revoked('Pending'))));
    const { fingerprint } = await confirmRevoke('ReaderReplaced');

    await waitFor(() => expect(bff.calls(REVOKE)).toHaveLength(1));
    expect(bff.calls(REVOKE)[0].body).toEqual({ reason: 'ReaderReplaced' });
    expect(await within(fingerprint).findByText('Revocada', {}, WAIT)).toBeInTheDocument();
    expect(within(fingerprint).queryByText('Activa')).not.toBeInTheDocument();
    expect(within(fingerprint).getByText(/Huella desactivada en SCI-PH\. Ya no sirve para identificar/)).toBeInTheDocument();
    expect(within(fingerprint).getByText('Limpieza del lector pendiente')).toBeInTheDocument();
    expect(within(fingerprint).getByText(/Huella desactivada en SCI-PH\. El lector borrará su copia/)).toBeInTheDocument();
    expect(within(fingerprint).queryByRole('button', { name: 'Revocar huella' })).not.toBeInTheDocument();
    expect(fingerprint.textContent).not.toMatch(/slot|r307|subjectReference/i);
  });

  it('sin motivo envía un cuerpo vacío; cancelar el diálogo no revoca nada', async () => {
    const { bff } = renderAs(RECEPTIONIST, withActive(() => json(200, revoked('Pending'))));
    const user = userEvent.setup();
    const fingerprint = await card();

    await user.click(await within(fingerprint).findByRole('button', { name: 'Revocar huella' }, WAIT));
    await user.click(within(await screen.findByRole('alertdialog', {}, WAIT)).getByRole('button', { name: 'Cancelar' }));
    expect(bff.calls(REVOKE)).toHaveLength(0);
    expect(within(fingerprint).getByText('Activa')).toBeInTheDocument();

    await confirmRevoke();
    await waitFor(() => expect(bff.calls(REVOKE)).toHaveLength(1));
    expect(bff.calls(REVOKE)[0].body).toEqual({});
  });

  it.each([
    ['Pending', 'Limpieza del lector pendiente', /El lector borrará su copia cuando se conecte/],
    ['Completed', 'Lector limpio', null],
    ['AttentionRequired', 'Requiere atención', /Lector no disponible o con fallos\. La huella ya está desactivada en SCI-PH/],
    ['NotRequired', 'Sin limpieza automática pendiente', null],
  ] as const)('una huella revocada con limpieza %s lo muestra sin parecer válida y sin botón de reintento', async (cleanup, badge, hint) => {
    const routes = { ...backend([enrollment('Pending')]).routes, 'GET /api/web/patients/p-1/fingerprints': () => json(200, [revoked(cleanup)]) };
    renderAs(RECEPTIONIST, routes);
    const fingerprint = await card();

    expect(await within(fingerprint).findByText(badge, {}, WAIT)).toBeInTheDocument();
    expect(within(fingerprint).getByText('Revocada')).toBeInTheDocument();
    expect(within(fingerprint).queryByText('Activa')).not.toBeInTheDocument();
    expect(within(fingerprint).getByText(/Huella desactivada en SCI-PH\./)).toBeInTheDocument();
    if (hint) expect(within(fingerprint).getByText(hint)).toBeInTheDocument();
    expect(within(fingerprint).queryByRole('button', { name: /Revocar huella|Reintentar/ })).not.toBeInTheDocument();
  });

  it('muestra los errores del BFF con mensajes seguros y la huella sigue activa', async () => {
    renderAs(RECEPTIONIST, withActive(() => problem(404, 'Not found.', { detail: 'PatientBiometricReference ref-1 was not found.' })));
    const { fingerprint } = await confirmRevoke();

    expect(await within(fingerprint).findByText('La huella no existe para este paciente.', {}, WAIT)).toBeInTheDocument();
    expect(within(fingerprint).getByText('Activa')).toBeInTheDocument();
  });

  it('sin Biometrics.Manage no hay huella ni revocación', async () => {
    const { bff } = renderAs(DOCTOR, withActive(() => json(200, revoked('Pending'))));

    expect(await screen.findByText('Ana Pérez', {}, WAIT)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Revocar huella' })).not.toBeInTheDocument();
    expect(bff.calls(REVOKE)).toHaveLength(0);
  });
});
