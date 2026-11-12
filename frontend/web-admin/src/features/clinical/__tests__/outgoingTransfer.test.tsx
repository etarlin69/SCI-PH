import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { AuthProvider, type WebUser } from '../../../auth';
import { ToastProvider } from '../../../components/shared';
import { AppRoutes } from '../../../routes';
import { createFakeBff, json, problem, session, type RecordedRequest } from '../../../test/fakeBff';
import { resetClinicSelectionForTests } from '../api/clinicSelection';
import type { ClinicalEncounterStatus, ClinicalEncounterSummary, ClinicalTransfer } from '../types/clinical.types';

// Permisos reales de RolePermissions (backend).
const DOCTOR: WebUser = {
  id: '77777777-7777-7777-7777-777777777777',
  userName: 'medico.origen',
  email: null,
  roles: ['Doctor'],
  permissions: ['Patients.Read', 'Encounters.Read', 'Medications.Read', 'Transfers.Read', 'Documents.Read', 'Patients.Identify', 'Encounters.Create', 'Encounters.Update', 'Encounters.Close', 'Triage.Write', 'VitalSigns.Write', 'ClinicalNotes.Write', 'Diagnoses.Write', 'Procedures.Write', 'Results.Write', 'Prescriptions.Write', 'MedicationAdministrations.Write', 'Transfers.Manage', 'Documents.Upload'],
};
const NURSE: WebUser = {
  id: '88888888-8888-8888-8888-888888888888',
  userName: 'enfermeria.origen',
  email: null,
  roles: ['Nurse'],
  permissions: ['Patients.Read', 'Encounters.Read', 'Medications.Read', 'Transfers.Read', 'Documents.Read', 'Encounters.Update', 'Triage.Write', 'VitalSigns.Write', 'ClinicalNotes.Write', 'Procedures.Write', 'MedicationAdministrations.Write', 'Documents.Upload'],
};

const WAIT = { timeout: 5000 };
const ENCOUNTER_ID = 'enc-1';
const CLINIC_A = { id: 'clinic-a', name: 'Hospital A' };
const CLINIC_B = { id: 'clinic-b', name: 'Hospital Receptor Demo' };

const arrival = (): ClinicalTransfer => ({
  id: 't-arrival',
  encounterId: ENCOUNTER_ID,
  status: 'Received',
  originClinic: null,
  destinationClinic: CLINIC_A,
  ambulance: null,
  requestedAtUtc: '2026-11-09T08:00:00Z',
  sentAtUtc: '2026-11-09T08:05:00Z',
  receivedAtUtc: '2026-11-09T08:30:00Z',
  patient: null,
  temporaryPatientCode: 'NN-20261109-ABCD',
  reason: 'Trauma mayor',
  latestTriagePriority: null,
});

/**
 * Web BFF con estado para una atención NN de Hospital A: el primer registro empieza la atención (Received → InProgress),
 * la solicitud exige InProgress/InEvolution y un solo traslado activo, el despacho pasa la atención a Transferred y la
 * cancelación de uno en camino la devuelve a InProgress. Mismos mensajes (detail) que el backend.
 */
function backend(initial: ClinicalEncounterStatus = 'Received') {
  const state = { status: initial, transfers: [arrival()] as ClinicalTransfer[], encounterGets: 0 };
  const encounter = (): ClinicalEncounterSummary => ({
    id: ENCOUNTER_ID,
    patientId: null,
    patient: null,
    temporaryPatientCode: 'NN-20261109-ABCD',
    clinicId: CLINIC_A.id,
    type: 'Prehospital',
    status: state.status,
    reason: 'Politrauma',
    startedAtUtc: '2026-11-09T07:50:00Z',
    endedAtUtc: null,
    latestTriagePriority: null,
  });
  const conflict = (detail: string) => problem(409, 'The request conflicts with the current state.', { detail });
  const find = (id: string) => state.transfers.find((t) => t.id === id)!;
  const routes: Record<string, (req: RecordedRequest) => Response> = {
    'GET /api/web/clinical/context': () => json(200, { staffId: 's-a', clinics: [CLINIC_A] }),
    [`GET /api/web/encounters/${ENCOUNTER_ID}`]: () => {
      state.encounterGets++;
      return json(200, encounter());
    },
    [`GET /api/web/encounters/${ENCOUNTER_ID}/notes`]: () => json(200, []),
    [`GET /api/web/encounters/${ENCOUNTER_ID}/diagnoses`]: () => json(200, []),
    [`POST /api/web/encounters/${ENCOUNTER_ID}/notes`]: (req) => {
      if (state.status === 'Received' || state.status === 'Created') state.status = 'InProgress';
      return json(201, { id: 'n1', encounterId: ENCOUNTER_ID, ...(req.body as object), authorStaffId: 's-a', authorDisplayName: null, recordedAtUtc: '2026-11-09T09:00:00Z' });
    },
    [`GET /api/web/encounters/${ENCOUNTER_ID}/transfers`]: () =>
      json(200, [...state.transfers].sort((a, b) => b.requestedAtUtc.localeCompare(a.requestedAtUtc))),
    'GET /api/web/transfers/destinations': () => json(200, [{ ...CLINIC_B, type: 'Hospital', municipality: 'Santiago', province: 'Santiago' }]),
    [`POST /api/web/encounters/${ENCOUNTER_ID}/transfers`]: (req) => {
      if (state.status !== 'InProgress' && state.status !== 'InEvolution') return conflict(`An encounter in status ${state.status} cannot be transferred.`);
      if (state.transfers.some((t) => t.status === 'Requested' || t.status === 'InTransit')) return conflict('The encounter already has an active transfer.');
      const body = req.body as { destinationClinicId: string; reason: string | null; notes: string | null };
      const created: ClinicalTransfer = {
        ...arrival(),
        id: 't-out',
        status: 'Requested',
        originClinic: CLINIC_A,
        destinationClinic: CLINIC_B,
        requestedAtUtc: '2026-11-09T10:00:00Z',
        sentAtUtc: null,
        receivedAtUtc: null,
        reason: body.reason,
        notes: body.notes,
        encounterStatus: state.status,
      };
      state.transfers.push(created);
      return json(201, created);
    },
    'POST /api/web/transfers/t-out/dispatch': () => {
      const t = find('t-out');
      if (t.status !== 'Requested') return conflict(`Dispatch requires a Requested transfer; current status is ${t.status}.`);
      t.status = 'InTransit';
      t.sentAtUtc = '2026-11-09T10:15:00Z';
      state.status = 'Transferred';
      return json(200, { ...t, encounterStatus: state.status });
    },
    'POST /api/web/transfers/t-out/cancel': () => {
      const t = find('t-out');
      if (t.status !== 'Requested' && t.status !== 'InTransit') return conflict(`A ${t.status} transfer cannot be cancelled.`);
      if (t.status === 'InTransit') state.status = 'InProgress';
      t.status = 'Cancelled';
      t.cancelledAtUtc = '2026-11-09T10:20:00Z';
      return json(200, { ...t, encounterStatus: state.status });
    },
  };
  return { state, routes };
}

function renderAs(user: WebUser, routes: Record<string, (req: RecordedRequest) => Response | Promise<Response>>) {
  resetClinicSelectionForTests();
  const bff = createFakeBff({
    'POST /api/web/auth/refresh': () => json(200, session(user)),
    'GET /api/web/auth/me': () => json(200, user),
    ...routes,
  });
  const view = render(
    <ToastProvider>
      <AuthProvider>
        <MemoryRouter initialEntries={[`/hospital/atenciones/${ENCOUNTER_ID}`]}>
          <AppRoutes />
        </MemoryRouter>
      </AuthProvider>
    </ToastProvider>,
  );
  return { bff, view };
}

const transferCard = async () => (await screen.findByRole('heading', { name: 'Traslado', level: 2 }, WAIT)).closest('section')!;
const headerStatus = (label: string) => screen.findAllByText(label, {}, WAIT);

async function writeFirstNote(user: ReturnType<typeof userEvent.setup>) {
  const notes = (await screen.findByRole('heading', { name: 'Notas clínicas' }, WAIT)).closest('section')!;
  await user.selectOptions(within(notes).getByLabelText(/Tipo de nota/), 'Evolution');
  await user.type(within(notes).getByLabelText(/Contenido/), 'Llega estable (dato de prueba).');
  await user.click(within(notes).getByRole('button', { name: 'Guardar nota' }));
}

describe('traslado saliente desde el expediente', () => {
  it('Recibida: no ofrece solicitar; el primer registro empieza la atención y habilita la solicitud', async () => {
    const { state, routes } = backend('Received');
    const { bff } = renderAs(DOCTOR, routes);
    const user = userEvent.setup();

    const card = await transferCard();
    expect(await within(card).findByText(/Registra primero la atención en este hospital/, {}, WAIT)).toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'Solicitar traslado' })).not.toBeInTheDocument();
    expect(bff.calls('GET /api/web/transfers/destinations')).toHaveLength(0);
    // La llegada prehospitalaria es historial.
    expect(within(card).getByText('Atención prehospitalaria → Hospital A')).toBeInTheDocument();

    const before = state.encounterGets;
    await writeFirstNote(user);

    await waitFor(() => expect(state.encounterGets).toBeGreaterThan(before), WAIT);
    expect(state.status).toBe('InProgress');
    expect((await headerStatus('En curso')).length).toBeGreaterThan(0);
    const reloaded = await transferCard();
    expect(await within(reloaded).findByRole('button', { name: 'Solicitar traslado' }, WAIT)).toBeInTheDocument();
    // Los destinos salen del endpoint de traslados (no del catálogo de administración), desde la clínica de la atención.
    const [destinations] = bff.calls('GET /api/web/transfers/destinations');
    expect(destinations.path).toBe('/api/web/transfers/destinations');
    expect(bff.requests.some((r) => r.path.startsWith('/api/web/admin/clinics'))).toBe(false);
  });

  it('solicita, despacha y cancela; cada paso persiste al recargar', { timeout: 20000 }, async () => {
    const { state, routes } = backend('InProgress');
    const { bff, view } = renderAs(DOCTOR, routes);
    const user = userEvent.setup();

    let card = await transferCard();
    await user.selectOptions(await within(card).findByLabelText(/Clínica de destino/, {}, WAIT), CLINIC_B.id);
    await user.type(within(card).getByLabelText(/Motivo/), 'Requiere neurocirugía');
    await user.type(within(card).getByLabelText(/Notas/), 'Paciente intubado');
    await user.click(within(card).getByRole('button', { name: 'Solicitar traslado' }));

    expect(await within(card).findByText(`Traslado solicitado a ${CLINIC_B.name}`, {}, WAIT)).toBeInTheDocument();
    const [request] = bff.calls(`POST /api/web/encounters/${ENCOUNTER_ID}/transfers`);
    expect(request.body).toEqual({ destinationClinicId: CLINIC_B.id, reason: 'Requiere neurocirugía', notes: 'Paciente intubado' });
    expect(within(card).getByText('Requiere neurocirugía')).toBeInTheDocument();
    expect(within(card).getByText('Paciente intubado')).toBeInTheDocument();

    // F5: el estado Solicitado se restaura desde el backend.
    view.unmount();
    renderAs(DOCTOR, routes);
    card = await transferCard();
    expect(await within(card).findByText(`Traslado solicitado a ${CLINIC_B.name}`, {}, WAIT)).toBeInTheDocument();

    await user.click(within(card).getByRole('button', { name: 'Despachar' }));
    await user.click(within(await screen.findByRole('alertdialog', {}, WAIT)).getByRole('button', { name: 'Despachar' }));

    // El despacho recarga la atención (la página se vuelve a montar): se buscan los elementos nuevos.
    await waitFor(() => expect(state.status).toBe('Transferred'), WAIT);
    expect(await screen.findByText(`En traslado a ${CLINIC_B.name}`, {}, WAIT)).toBeInTheDocument();
    card = await transferCard();
    expect(within(card).queryByRole('button', { name: 'Despachar' })).not.toBeInTheDocument();
    expect((await headerStatus('En traslado')).length).toBeGreaterThan(0);
    // En traslado la atención no admite registros: no hay formulario de notas.
    expect(screen.queryByRole('button', { name: 'Guardar nota' })).not.toBeInTheDocument();

    await user.click(within(card).getByRole('button', { name: 'Cancelar traslado' }));
    await user.click(within(await screen.findByRole('alertdialog', {}, WAIT)).getByRole('button', { name: 'Cancelar traslado' }));

    await waitFor(() => expect(state.status).toBe('InProgress'), WAIT);
    expect(await screen.findByRole('button', { name: 'Solicitar traslado' }, WAIT)).toBeInTheDocument();
    card = await transferCard();
    const history = within(card).getByRole('region', { name: 'Historial de traslados' });
    expect(within(history).getByText(`Hospital A → ${CLINIC_B.name}`)).toBeInTheDocument();
    expect(within(history).getByText('Cancelado')).toBeInTheDocument();
  });

  it('un 409 del backend se muestra en español y la tarjeta sigue visible', async () => {
    const { state, routes } = backend('InProgress');
    renderAs(DOCTOR, {
      ...routes,
      [`POST /api/web/encounters/${ENCOUNTER_ID}/transfers`]: () =>
        problem(409, 'The request conflicts with the current state.', { detail: 'The encounter already has an active transfer.' }),
    });
    const user = userEvent.setup();

    const card = await transferCard();
    await user.selectOptions(await within(card).findByLabelText(/Clínica de destino/, {}, WAIT), CLINIC_B.id);
    await user.click(within(card).getByRole('button', { name: 'Solicitar traslado' }));

    expect(await screen.findByText('La atención ya tiene un traslado activo.', {}, WAIT)).toBeInTheDocument();
    expect(await transferCard()).toBeInTheDocument();
    expect(within(card).getByRole('button', { name: 'Solicitar traslado' })).toBeInTheDocument();
    expect(state.transfers).toHaveLength(1);
  });

  it('un despacho que pierde contra otro usuario (409) recarga el traslado sin pantalla en blanco', async () => {
    const { state, routes } = backend('InProgress');
    state.transfers.push({ ...arrival(), id: 't-out', status: 'Requested', originClinic: CLINIC_A, destinationClinic: CLINIC_B, requestedAtUtc: '2026-11-09T10:00:00Z', sentAtUtc: null, receivedAtUtc: null });
    renderAs(DOCTOR, {
      ...routes,
      'POST /api/web/transfers/t-out/dispatch': () => {
        state.transfers.find((t) => t.id === 't-out')!.status = 'Cancelled';
        return problem(409, 'The request conflicts with the current state.', { detail: 'Dispatch requires a Requested transfer; current status is Cancelled.' });
      },
    });
    const user = userEvent.setup();

    const card = await transferCard();
    await user.click(await within(card).findByRole('button', { name: 'Despachar' }, WAIT));
    await user.click(within(await screen.findByRole('alertdialog', {}, WAIT)).getByRole('button', { name: 'Despachar' }));

    expect(await screen.findByText('El traslado ya fue despachado, recibido o cancelado.', {}, WAIT)).toBeInTheDocument();
    expect(await within(card).findByRole('button', { name: 'Solicitar traslado' }, WAIT)).toBeInTheDocument();
  });

  it('403 al leer los traslados: error con reintento, sin afectar el resto del expediente', async () => {
    const { routes } = backend('InProgress');
    renderAs(DOCTOR, { ...routes, [`GET /api/web/encounters/${ENCOUNTER_ID}/transfers`]: () => problem(403, 'Forbidden') });

    const card = await transferCard();
    // ErrorState añade el punto final al título: un solo punto, nunca «atención..».
    const title = await within(card).findByText('No se pudieron cargar los traslados de la atención.', { exact: false }, WAIT);
    expect(title.textContent).toBe('No se pudieron cargar los traslados de la atención.');
    expect(within(card).getByRole('button', { name: /Reintentar/ })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Notas clínicas' })).toBeInTheDocument();
  });

  it('muestra la carga de destinos y el vacío cuando no hay otra clínica', async () => {
    const { routes } = backend('InProgress');
    let release!: () => void;
    const gate = new Promise<void>((r) => (release = r));
    renderAs(DOCTOR, {
      ...routes,
      'GET /api/web/transfers/destinations': async () => {
        await gate;
        return json(200, []);
      },
    });

    const card = await transferCard();
    expect(await within(card).findByText('Cargando clínicas de destino…', {}, WAIT)).toBeInTheDocument();
    release();
    expect(await within(card).findByText('No hay otras clínicas activas a las que trasladar', {}, WAIT)).toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'Solicitar traslado' })).not.toBeInTheDocument();
  });

  it('sin Transfers.Manage (enfermería) ve el traslado activo y el historial pero ninguna acción', async () => {
    const { state, routes } = backend('InProgress');
    state.transfers.push({ ...arrival(), id: 't-out', status: 'InTransit', originClinic: CLINIC_A, destinationClinic: CLINIC_B, requestedAtUtc: '2026-11-09T10:00:00Z', sentAtUtc: '2026-11-09T10:15:00Z', receivedAtUtc: null });
    const { bff } = renderAs(NURSE, routes);

    const card = await transferCard();
    expect(await within(card).findByText(`En traslado a ${CLINIC_B.name}`, {}, WAIT)).toBeInTheDocument();
    expect(within(card).getByText('Atención prehospitalaria → Hospital A')).toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: /Despachar|Cancelar traslado|Solicitar traslado/ })).not.toBeInTheDocument();
    expect(bff.calls('GET /api/web/transfers/destinations')).toHaveLength(0);
  });

  it('una atención cerrada no ofrece traslado', async () => {
    const { routes } = backend('Closed');
    renderAs(DOCTOR, routes);

    const card = await transferCard();
    expect(await within(card).findByText('La atención está cerrada.', {}, WAIT)).toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'Solicitar traslado' })).not.toBeInTheDocument();
  });
});
