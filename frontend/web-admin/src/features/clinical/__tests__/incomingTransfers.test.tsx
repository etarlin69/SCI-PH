import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { AuthProvider, type WebUser } from '../../../auth';
import { ToastProvider } from '../../../components/shared';
import { AppRoutes } from '../../../routes';
import { createFakeBff, json, problem, session, type RecordedRequest } from '../../../test/fakeBff';
import { resetClinicSelectionForTests } from '../api/clinicSelection';
import type { ClinicalTransfer } from '../types/clinical.types';

// Permisos reales de RolePermissions (backend).
const DOCTOR: WebUser = {
  id: '55555555-5555-5555-5555-555555555555',
  userName: 'medico.destino',
  email: null,
  roles: ['Doctor'],
  permissions: ['Patients.Read', 'Encounters.Read', 'Medications.Read', 'Transfers.Read', 'Documents.Read', 'Patients.Identify', 'Encounters.Create', 'Encounters.Update', 'Encounters.Close', 'Triage.Write', 'VitalSigns.Write', 'ClinicalNotes.Write', 'Diagnoses.Write', 'Procedures.Write', 'Results.Write', 'Prescriptions.Write', 'MedicationAdministrations.Write', 'Transfers.Manage', 'Documents.Upload'],
};
const NURSE: WebUser = {
  id: '66666666-6666-6666-6666-666666666666',
  userName: 'enfermeria.destino',
  email: null,
  roles: ['Nurse'],
  permissions: ['Patients.Read', 'Encounters.Read', 'Medications.Read', 'Transfers.Read', 'Documents.Read', 'Encounters.Update', 'Triage.Write', 'VitalSigns.Write', 'ClinicalNotes.Write', 'Procedures.Write', 'MedicationAdministrations.Write', 'Documents.Upload'],
};
const RECEPTIONIST: WebUser = {
  id: '44444444-4444-4444-4444-444444444444',
  userName: 'recepcion.destino',
  email: null,
  roles: ['Receptionist'],
  permissions: ['Patients.Read', 'Patients.Create', 'Patients.Update', 'Patients.Identify', 'Accounts.InvitePatient', 'Encounters.Create', 'Transfers.Read', 'Transfers.Manage', 'Documents.Upload'],
};

const WAIT = { timeout: 5000 };
const transfer = (id: string, status: ClinicalTransfer['status']): ClinicalTransfer => ({
  id,
  encounterId: `enc-${id}`,
  status,
  originClinic: { id: 'clinic-a', name: 'Hospital Origen' },
  destinationClinic: { id: 'clinic-b', name: 'Hospital Destino' },
  ambulance: null,
  requestedAtUtc: '2026-10-12T10:00:00Z',
  sentAtUtc: status === 'InTransit' ? '2026-10-12T10:10:00Z' : null,
  receivedAtUtc: null,
  patient: { id: 'p1', displayName: 'Paciente Trasladado', birthDate: null, biologicalSex: 'Female', identityStatus: 'Verified', primaryIdentifier: { type: 'NationalId', value: '***5678' } },
  temporaryPatientCode: null,
  reason: 'Requiere UCI',
  latestTriagePriority: null,
  encounterStatus: status === 'InTransit' ? 'Transferred' : 'InProgress',
});

// Backend con estado: recibir saca el traslado de la lista entrante, también después de recargar.
function statefulBackend() {
  const incoming: ClinicalTransfer[] = [transfer('t-transit', 'InTransit'), transfer('t-requested', 'Requested')];
  return {
    incoming,
    routes: {
      'GET /api/web/clinical/context': () => json(200, { staffId: 's-b', clinics: [{ id: 'clinic-b', name: 'Hospital Destino' }] }),
      'GET /api/web/transfers/incoming': () => json(200, incoming),
      'GET /api/web/admissions/encounters': () => json(200, []),
      'POST /api/web/transfers/t-transit/receive': () => {
        const index = incoming.findIndex((t) => t.id === 't-transit');
        if (index < 0) return problem(409, 'The request conflicts with the current state.', { detail: 'Receive requires a InTransit transfer; current status is Received.' });
        const [received] = incoming.splice(index, 1);
        return json(200, { ...received, status: 'Received', encounterStatus: 'Received', receivedByStaffId: 's-b' });
      },
    } as Record<string, (req: RecordedRequest) => Response>,
  };
}

function renderAs(user: WebUser, routes: Record<string, (req: RecordedRequest) => Response>, path = '/hospital/transferencias') {
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

const panel = async () => (await screen.findByRole('heading', { name: 'Traslados entrantes' }, WAIT)).closest('section')!;

describe('traslados entrantes reales', () => {
  it('lista los traslados del BFF, recibe el que está en camino y tras F5 ya no aparece', async () => {
    const backend = statefulBackend();
    const { bff, view } = renderAs(DOCTOR, backend.routes);
    const user = userEvent.setup();

    const card = await panel();
    expect(await within(card).findAllByText('Paciente Trasladado', {}, WAIT)).toHaveLength(2);
    expect(within(card).queryByText('Simulación')).not.toBeInTheDocument();
    expect(within(card).getAllByText(/Origen: Hospital Origen/)).toHaveLength(2);
    const buttons = within(card).getAllByRole('button', { name: 'Registrar llegada' });
    // Solo el traslado en camino puede recibirse; el solicitado aún no salió.
    expect(buttons.filter((b) => !b.hasAttribute('disabled'))).toHaveLength(1);

    await user.click(buttons.find((b) => !b.hasAttribute('disabled'))!);
    const dialog = await screen.findByRole('alertdialog', {}, WAIT);
    await user.click(within(dialog).getByRole('button', { name: 'Registrar llegada' }));

    await waitFor(() => expect(bff.calls('POST /api/web/transfers/t-transit/receive')).toHaveLength(1));
    expect(bff.calls('POST /api/web/transfers/t-transit/receive')[0].body).toBeUndefined();
    expect(await within(card).findByText(/la atención está ahora a cargo de este hospital/, {}, WAIT)).toBeInTheDocument();
    expect(within(card).getByRole('link', { name: 'Abrir la atención' })).toHaveAttribute('href', '/hospital/atenciones/enc-t-transit');
    await waitFor(() => expect(within(card).getAllByText('Paciente Trasladado')).toHaveLength(1));

    // F5: una aplicación nueva lee el mismo backend; el recibido ya no ofrece la acción.
    view.unmount();
    renderAs(DOCTOR, backend.routes);
    const reloaded = await panel();
    expect(await within(reloaded).findAllByText('Paciente Trasladado', {}, WAIT)).toHaveLength(1);
    expect(within(reloaded).getByRole('button', { name: 'Registrar llegada' })).toBeDisabled();
  });

  it('sin Transfers.Manage no se ofrece la acción; recepción la tiene pero no el enlace clínico', async () => {
    const { view } = renderAs(NURSE, statefulBackend().routes);
    const card = await panel();
    expect(await within(card).findAllByText('Paciente Trasladado', {}, WAIT)).toHaveLength(2);
    expect(within(card).queryByRole('button', { name: 'Registrar llegada' })).not.toBeInTheDocument();
    view.unmount();

    const user = userEvent.setup();
    renderAs(RECEPTIONIST, statefulBackend().routes, '/hospital/recepcion');
    const receptionCard = await panel();
    await user.click((await within(receptionCard).findAllByRole('button', { name: 'Registrar llegada' }, WAIT)).find((b) => !b.hasAttribute('disabled'))!);
    await user.click(within(await screen.findByRole('alertdialog', {}, WAIT)).getByRole('button', { name: 'Registrar llegada' }));
    expect(await within(receptionCard).findByText(/la atención está ahora a cargo de este hospital/, {}, WAIT)).toBeInTheDocument();
    expect(within(receptionCard).queryByRole('link', { name: 'Abrir la atención' })).not.toBeInTheDocument();
  });

  it('muestra los errores 409 y 403 del BFF', async () => {
    const user = userEvent.setup();
    const { view } = renderAs(DOCTOR, {
      ...statefulBackend().routes,
      'POST /api/web/transfers/t-transit/receive': () =>
        problem(409, 'The request conflicts with the current state.', { detail: 'Receive requires a InTransit transfer; current status is Received.' }),
    });
    const card = await panel();
    await user.click((await within(card).findAllByRole('button', { name: 'Registrar llegada' }, WAIT)).find((b) => !b.hasAttribute('disabled'))!);
    await user.click(within(await screen.findByRole('alertdialog', {}, WAIT)).getByRole('button', { name: 'Registrar llegada' }));
    expect(await screen.findByText('El traslado ya fue recibido o todavía no está en camino.', {}, WAIT)).toBeInTheDocument();
    view.unmount();

    renderAs(DOCTOR, {
      ...statefulBackend().routes,
      'GET /api/web/transfers/incoming': () =>
        problem(403, 'Access denied.', { detail: 'The user is not affiliated with the requested clinic.' }),
    });
    expect(await screen.findByText(/No se pudieron cargar los traslados/, {}, WAIT)).toBeInTheDocument();
    expect(screen.getByText(/El registro pertenece a una clínica a la que no estás afiliado/)).toBeInTheDocument();
  });

  // Traslado prehospitalario del Mobile BFF: sin clínica de origen y con paciente NN (solo el código temporal).
  it('muestra el origen prehospitalario y el paciente NN, y recibe solo el que está en camino', async () => {
    const prehospital = (id: string, status: ClinicalTransfer['status']): ClinicalTransfer => ({
      ...transfer(id, status),
      originClinic: null,
      patient: null,
      temporaryPatientCode: `NN-20261027-${id.toUpperCase()}`,
    });
    const incoming = [prehospital('ph-transit', 'InTransit'), prehospital('ph-requested', 'Requested')];
    const user = userEvent.setup();
    const { bff } = renderAs(DOCTOR, {
      ...statefulBackend().routes,
      'GET /api/web/transfers/incoming': () => json(200, incoming),
      'POST /api/web/transfers/ph-transit/receive': () => json(200, { ...incoming[0], status: 'Received', encounterStatus: 'Received' }),
    });

    const card = await panel();
    expect(await within(card).findAllByText(/Origen: Atención prehospitalaria/, {}, WAIT)).toHaveLength(2);
    expect(within(card).getAllByText(/Paciente NN/)).toHaveLength(2);
    expect(within(card).getAllByText('No identificado')).toHaveLength(2);
    const buttons = within(card).getAllByRole('button', { name: 'Registrar llegada' });
    expect(buttons.filter((b) => !b.hasAttribute('disabled'))).toHaveLength(1);

    await user.click(buttons.find((b) => !b.hasAttribute('disabled'))!);
    await user.click(within(await screen.findByRole('alertdialog', {}, WAIT)).getByRole('button', { name: 'Registrar llegada' }));

    await waitFor(() => expect(bff.calls('POST /api/web/transfers/ph-transit/receive')).toHaveLength(1));
    expect(bff.calls('POST /api/web/transfers/ph-requested/receive')).toHaveLength(0);
    expect(await within(card).findByRole('link', { name: 'Abrir la atención' }, WAIT)).toHaveAttribute('href', '/hospital/atenciones/enc-ph-transit');
  });
});
