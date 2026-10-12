import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { AuthProvider, Permissions, resolveHomeRoute, type WebUser } from '../../../auth';
import { ToastProvider } from '../../../components/shared';
import { AppRoutes } from '../../../routes';
import { ADMIN_USER, NURSE_USER, createFakeBff, json, session } from '../../../test/fakeBff';
import { resetClinicSelectionForTests } from '../api/clinicSelection';

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

  it('recepción lista la admisión real (sin datos clínicos) y solo marca «Simulación» en lo que aún es mock', async () => {
    resetClinicSelectionForTests();
    createFakeBff({
      'POST /api/web/auth/refresh': () => json(200, session(RECEPTIONIST)),
      'GET /api/web/auth/me': () => json(200, RECEPTIONIST),
      'GET /api/web/clinical/context': () => json(200, { staffId: 's1', clinics: [{ id: 'c1', name: 'Hospital Real' }] }),
      'GET /api/web/admissions/encounters': () =>
        json(200, [
          {
            encounterId: 'e1',
            patientId: null,
            patientDisplayName: null,
            temporaryPatientCode: 'NN-0123456789ABCDEF0123456789ABCDEF',
            type: 'Emergency',
            status: 'Created',
            startedAtUtc: '2026-10-07T10:00:00Z',
            clinicId: 'c1',
          },
        ]),
    });
    render(
      <ToastProvider>
        <AuthProvider>
          <MemoryRouter initialEntries={['/hospital/recepcion']}>
            <AppRoutes />
          </MemoryRouter>
        </AuthProvider>
      </ToastProvider>,
    );

    expect(await screen.findByText('Hospital Real', {}, WAIT)).toBeInTheDocument();
    const admissions = (await screen.findByRole('heading', { name: 'Atenciones en admisión' }, WAIT)).closest('section')!;
    expect(await within(admissions).findByRole('button', { name: 'Identificar' }, WAIT)).toBeInTheDocument();
    expect(within(admissions).queryByText('Simulación')).not.toBeInTheDocument();
    const transfers = screen.getByRole('heading', { name: 'Traslados entrantes' }).closest('section')!;
    expect(within(transfers).getByText('Simulación')).toBeInTheDocument();
    expect(screen.getAllByText('Simulación')).toHaveLength(1);
  });

  // Mismo rol en ambos casos: la acción depende solo del permiso real Encounters.Create, nunca del rol.
  it.each([
    { label: 'con Encounters.Create ve', permissions: [...NURSE_USER.permissions, 'Encounters.Create'], visible: true },
    { label: 'sin Encounters.Create no ve', permissions: NURSE_USER.permissions, visible: false },
  ])('en Atenciones, un usuario $label «Nueva atención»', async ({ permissions, visible }) => {
    resetClinicSelectionForTests();
    const user: WebUser = { ...NURSE_USER, permissions };
    createFakeBff({
      'POST /api/web/auth/refresh': () => json(200, session(user)),
      'GET /api/web/auth/me': () => json(200, user),
      'GET /api/web/clinical/context': () => json(200, { staffId: 's1', clinics: [{ id: 'c1', name: 'Hospital Real' }] }),
      'GET /api/web/encounters': () => json(200, []),
    });
    render(
      <ToastProvider>
        <AuthProvider>
          <MemoryRouter initialEntries={['/hospital/atenciones']}>
            <AppRoutes />
          </MemoryRouter>
        </AuthProvider>
      </ToastProvider>,
    );

    expect(await screen.findByRole('heading', { name: 'Atenciones' }, WAIT)).toBeInTheDocument();
    expect(await screen.findByText('No hay atenciones abiertas', {}, WAIT)).toBeInTheDocument();
    if (visible) expect(screen.getByRole('link', { name: 'Nueva atención' })).toHaveAttribute('href', '/hospital/recepcion');
    else expect(screen.queryByRole('link', { name: 'Nueva atención' })).not.toBeInTheDocument();
  });

  // Permisos reales de RolePermissions.Doctor.
  const DOCTOR: WebUser = {
    id: '55555555-5555-5555-5555-555555555555',
    userName: 'medico.prueba',
    email: null,
    roles: ['Doctor'],
    permissions: ['Patients.Read', 'Encounters.Read', 'Medications.Read', 'Transfers.Read', 'Documents.Read', 'Patients.Identify', 'Encounters.Create', 'Encounters.Update', 'Encounters.Close', 'Triage.Write', 'VitalSigns.Write', 'ClinicalNotes.Write', 'Diagnoses.Write', 'Procedures.Write', 'Results.Write', 'Prescriptions.Write', 'MedicationAdministrations.Write', 'Transfers.Manage', 'Documents.Upload'],
  };
  const ENC = 'e1';
  const encounterIn = (status: string) => ({
    id: ENC, patientId: 'p1', temporaryPatientCode: null, clinicId: 'c1', type: 'Emergency', status, reason: null,
    startedAtUtc: '2026-10-08T10:00:00Z', endedAtUtc: null, latestTriagePriority: null,
    patient: { id: 'p1', displayName: 'Paciente Real', birthDate: null, biologicalSex: 'Female', identityStatus: 'Unverified', primaryIdentifier: null },
  });
  const renderEncounter = (user: WebUser, path: string, status = 'InProgress') => {
    resetClinicSelectionForTests();
    const bff = createFakeBff({
      'POST /api/web/auth/refresh': () => json(200, session(user)),
      'GET /api/web/auth/me': () => json(200, user),
      [`GET /api/web/encounters/${ENC}`]: () => json(200, encounterIn(status)),
      [`GET /api/web/encounters/${ENC}/notes`]: () => json(200, [{ id: 'n1', encounterId: ENC, type: 'Evolution', content: 'Nota guardada en PostgreSQL', occurredAtUtc: '2026-10-08T10:05:00Z', createdAtUtc: '2026-10-08T10:05:01Z', authorStaffId: 's1', authorDisplayName: 'Dra. Prueba' }]),
      [`GET /api/web/encounters/${ENC}/diagnoses`]: () => json(200, [{ id: 'd1', encounterId: ENC, description: 'Neumonía', codeSystem: 'ICD-10', code: 'J18.9', type: 'Definitive', isPrimary: true, recordedAtUtc: '2026-10-08T10:06:00Z', confirmedAtUtc: '2026-10-08T10:06:00Z', recordedByStaffId: 's1' }]),
      [`POST /api/web/encounters/${ENC}/notes`]: () => json(201, { id: 'n2' }),
    });
    render(
      <ToastProvider>
        <AuthProvider>
          <MemoryRouter initialEntries={[path]}>
            <AppRoutes />
          </MemoryRouter>
        </AuthProvider>
      </ToastProvider>,
    );
    return bff;
  };

  it('el expediente muestra notas y diagnósticos reales del BFF, sin «Simulación», y crea una nota real', async () => {
    const bff = renderEncounter(DOCTOR, `/hospital/atenciones/${ENC}`);

    expect(await screen.findByText('Nota guardada en PostgreSQL', {}, WAIT)).toBeInTheDocument();
    expect(await screen.findByText('Neumonía', { exact: false }, WAIT)).toBeInTheDocument();
    const notesCard = screen.getByRole('heading', { name: 'Notas clínicas' }).closest('section')!;
    expect(within(notesCard).queryByText('Simulación')).not.toBeInTheDocument();
    expect(within(screen.getByRole('heading', { name: 'Diagnósticos' }).closest('section')!).queryByText('Simulación')).not.toBeInTheDocument();

    const user = userEvent.setup();
    await user.selectOptions(within(notesCard).getByLabelText(/Tipo de nota/), 'Evolution');
    await user.type(within(notesCard).getByLabelText(/Contenido/), 'Evolución favorable');
    await user.click(within(notesCard).getByRole('button', { name: 'Guardar nota' }));

    await waitFor(() => expect(bff.calls(`POST /api/web/encounters/${ENC}/notes`)).toHaveLength(1));
    const sent = bff.calls(`POST /api/web/encounters/${ENC}/notes`)[0].body as Record<string, unknown>;
    expect(sent).toMatchObject({ type: 'Evolution', content: 'Evolución favorable' });
    expect(Object.keys(sent).sort()).toEqual(['content', 'occurredAtUtc', 'type']);
  });

  it.each([
    { label: 'con Encounters.Close puede cerrar una atención en curso', permissions: DOCTOR.permissions, status: 'InProgress', visible: true, enabled: true },
    { label: 'con Encounters.Close no puede cerrar una atención recién creada', permissions: DOCTOR.permissions, status: 'Created', visible: true, enabled: false },
    { label: 'sin Encounters.Close no ve la acción', permissions: DOCTOR.permissions.filter((p) => p !== 'Encounters.Close'), status: 'InProgress', visible: false, enabled: false },
  ])('egreso: un médico $label', async ({ permissions, status, visible, enabled }) => {
    renderEncounter({ ...DOCTOR, permissions }, `/hospital/atenciones/${ENC}/medicacion`, status);

    const discharge = (await screen.findByRole('heading', { name: 'Egreso' }, WAIT)).closest('section')!;
    expect(within(discharge).queryByText('Simulación')).not.toBeInTheDocument();
    const button = within(discharge).queryByRole('button', { name: 'Cerrar atención' });
    if (!visible) {
      expect(button).not.toBeInTheDocument();
      expect(within(discharge).getByText(/requiere el permiso Encounters.Close/)).toBeInTheDocument();
    } else if (enabled) expect(button).toBeEnabled();
    else expect(button).toBeDisabled();
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
