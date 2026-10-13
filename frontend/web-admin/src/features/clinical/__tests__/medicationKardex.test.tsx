import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { AuthProvider, type WebUser } from '../../../auth';
import { ToastProvider } from '../../../components/shared';
import { AppRoutes } from '../../../routes';
import { createFakeBff, json, problem, session, type RecordedRequest } from '../../../test/fakeBff';
import { resetClinicSelectionForTests } from '../api/clinicSelection';
import type { MedicationAdministration, Prescription } from '../types/clinical.types';

// Permisos reales de RolePermissions (backend).
const DOCTOR: WebUser = {
  id: '55555555-5555-5555-5555-555555555555',
  userName: 'medico.prueba',
  email: null,
  roles: ['Doctor'],
  permissions: ['Patients.Read', 'Encounters.Read', 'Medications.Read', 'Transfers.Read', 'Documents.Read', 'Patients.Identify', 'Encounters.Create', 'Encounters.Update', 'Encounters.Close', 'Triage.Write', 'VitalSigns.Write', 'ClinicalNotes.Write', 'Diagnoses.Write', 'Procedures.Write', 'Results.Write', 'Prescriptions.Write', 'MedicationAdministrations.Write', 'Transfers.Manage', 'Documents.Upload'],
};
const NURSE: WebUser = {
  id: '66666666-6666-6666-6666-666666666666',
  userName: 'enfermeria.prueba',
  email: null,
  roles: ['Nurse'],
  permissions: ['Patients.Read', 'Encounters.Read', 'Medications.Read', 'Transfers.Read', 'Documents.Read', 'Encounters.Update', 'Triage.Write', 'VitalSigns.Write', 'ClinicalNotes.Write', 'Procedures.Write', 'MedicationAdministrations.Write', 'Documents.Upload'],
};

const ENC = 'e1';
const PATH = `/hospital/atenciones/${ENC}/medicacion`;
const WAIT = { timeout: 5000 };
const PARACETAMOL = { id: 'med-1', code: 'DEV-PARACETAMOL-500', name: 'Paracetamol', presentation: 'Tableta', concentration: '500 mg', defaultRoute: 'Oral', status: 'Active' };
const RETIRED = { id: 'med-2', code: 'DEV-RETIRADO', name: 'Retirado', presentation: null, concentration: null, defaultRoute: null, status: 'Inactive' };

const encounterIn = (status: string) => ({
  id: ENC, patientId: 'p1', temporaryPatientCode: null, clinicId: 'c1', type: 'Emergency', status, reason: null,
  startedAtUtc: '2026-10-08T10:00:00Z', endedAtUtc: status === 'Closed' ? '2026-10-08T12:00:00Z' : null, latestTriagePriority: null,
  patient: { id: 'p1', displayName: 'Paciente Real', birthDate: null, biologicalSex: 'Female', identityStatus: 'Unverified', primaryIdentifier: null },
});

// Backend con estado: lo que se guarda por POST es lo que devuelven los GET, también después de recargar la página.
function statefulBackend(status = 'InProgress') {
  const prescriptions: Prescription[] = [];
  const administrations: MedicationAdministration[] = [];
  return {
    prescriptions,
    administrations,
    routes: {
      [`GET /api/web/encounters/${ENC}`]: () => json(200, encounterIn(status)),
      'GET /api/web/medications': () => json(200, [PARACETAMOL, RETIRED]),
      [`GET /api/web/encounters/${ENC}/prescriptions`]: () => json(200, prescriptions),
      [`GET /api/web/encounters/${ENC}/medication-administrations`]: () => json(200, administrations),
      [`POST /api/web/encounters/${ENC}/prescriptions`]: (req: RecordedRequest) => {
        const body = req.body as { notes: string | null; details: Prescription['details'] };
        const saved: Prescription = {
          id: `rx-${prescriptions.length + 1}`, encounterId: ENC, status: 'Issued', notes: body.notes,
          createdAtUtc: '2026-10-08T10:30:00Z', issuedAtUtc: '2026-10-08T10:30:00Z', prescribedByStaffId: 'staff-doctor',
          details: body.details.map((d, i) => ({ ...d, id: `line-${i + 1}`, medicationName: 'Paracetamol' })),
        };
        prescriptions.unshift(saved);
        return json(201, saved);
      },
      [`POST /api/web/encounters/${ENC}/medication-administrations`]: (req: RecordedRequest) => {
        const body = req.body as Omit<MedicationAdministration, 'id' | 'encounterId' | 'medicationName' | 'status'>;
        const saved: MedicationAdministration = {
          ...body, id: `adm-${administrations.length + 1}`, encounterId: ENC, medicationName: 'Paracetamol', status: 'Completed', administeredByStaffId: 'staff-nurse',
        };
        administrations.unshift(saved);
        return json(201, saved);
      },
    } as Record<string, (req: RecordedRequest) => Response>,
  };
}

function renderAs(user: WebUser, routes: Record<string, (req: RecordedRequest) => Response>) {
  resetClinicSelectionForTests();
  const bff = createFakeBff({
    'POST /api/web/auth/refresh': () => json(200, session(user)),
    'GET /api/web/auth/me': () => json(200, user),
    ...routes,
  });
  const view = render(
    <ToastProvider>
      <AuthProvider>
        <MemoryRouter initialEntries={[PATH]}>
          <AppRoutes />
        </MemoryRouter>
      </AuthProvider>
    </ToastProvider>,
  );
  return { bff, view };
}

const card = async (title: string) => (await screen.findByRole('heading', { name: title }, WAIT)).closest('section')!;

describe('medicación real: catálogo, prescripciones y Kardex', () => {
  it('prescribe con el catálogo HTTP, administra la línea y el Kardex sigue igual tras recargar (F5)', async () => {
    const backend = statefulBackend();
    const { bff, view } = renderAs(DOCTOR, backend.routes);
    const user = userEvent.setup();

    const prescriptionsCard = await card('Prescripciones');
    const kardexCard = await card('Administración de medicamentos (Kardex)');
    expect(within(prescriptionsCard).queryByText('Simulación')).not.toBeInTheDocument();
    expect(within(kardexCard).queryByText('Simulación')).not.toBeInTheDocument();

    // Catálogo real: el inactivo aparece pero no se puede elegir.
    const medication = within(prescriptionsCard).getByLabelText(/Medicamento/);
    await waitFor(() => expect(medication).toBeEnabled(), WAIT);
    expect(within(medication).getByRole('option', { name: /Retirado.*inactivo/ })).toBeDisabled();
    await user.selectOptions(medication, PARACETAMOL.id);
    await user.type(within(prescriptionsCard).getByLabelText(/^Dosis/), '500');
    await user.type(within(prescriptionsCard).getByLabelText(/Unidad/), 'mg');
    await user.type(within(prescriptionsCard).getByLabelText(/Frecuencia/), 'cada 8 horas');
    await user.click(within(prescriptionsCard).getByRole('button', { name: 'Emitir prescripción' }));

    await waitFor(() => expect(bff.calls(`POST /api/web/encounters/${ENC}/prescriptions`)).toHaveLength(1));
    const sent = bff.calls(`POST /api/web/encounters/${ENC}/prescriptions`)[0].body as { details: Record<string, unknown>[] };
    expect(Object.keys(sent).sort()).toEqual(['details', 'notes']);
    // La vía habitual del catálogo se propuso; el prescriptor no viaja desde el cliente.
    expect(sent.details).toEqual([{ medicationId: PARACETAMOL.id, dose: 500, doseUnit: 'mg', route: 'Oral', frequency: 'cada 8 horas', durationDays: null, instructions: null }]);
    expect(await within(prescriptionsCard).findByText(/500 mg, Oral, cada 8 horas/, {}, WAIT)).toBeInTheDocument();
    expect(await within(kardexCard).findByText('Sin dosis administradas', {}, WAIT)).toBeInTheDocument();

    // Administración de la línea prescrita: medicamento, dosis, unidad y vía vienen de la línea.
    await user.selectOptions(within(kardexCard).getByLabelText(/Línea de tratamiento/), 'line-1');
    expect(within(kardexCard).getByLabelText(/Dosis administrada/)).toHaveValue(500);
    await user.click(within(kardexCard).getByRole('button', { name: 'Registrar administración' }));

    await waitFor(() => expect(bff.calls(`POST /api/web/encounters/${ENC}/medication-administrations`)).toHaveLength(1));
    const dose = bff.calls(`POST /api/web/encounters/${ENC}/medication-administrations`)[0].body as Record<string, unknown>;
    expect(dose).toMatchObject({ medicationId: PARACETAMOL.id, dose: 500, doseUnit: 'mg', route: 'Oral', prescriptionId: 'rx-1', prescriptionDetailId: 'line-1' });
    expect(dose).not.toHaveProperty('administeredByStaffId');
    expect(await within(kardexCard).findByText(/1 dosis administrada/, {}, WAIT)).toBeInTheDocument();

    // F5: una aplicación nueva lee el mismo backend.
    view.unmount();
    renderAs(DOCTOR, backend.routes);
    const reloaded = await card('Administración de medicamentos (Kardex)');
    expect(await within(reloaded).findByText(/1 dosis administrada/, {}, WAIT)).toBeInTheDocument();
    expect(within(reloaded).getByText('Paracetamol — 500 mg, Oral')).toBeInTheDocument();
    expect(within(await card('Prescripciones')).getByText('Emitida')).toBeInTheDocument();
  });

  it('una enfermera administra pero no ve la acción de prescribir; sin MedicationAdministrations.Write no registra dosis', async () => {
    const { view } = renderAs(NURSE, statefulBackend().routes);
    expect(await within(await card('Administración de medicamentos (Kardex)')).findByRole('button', { name: 'Registrar administración' }, WAIT)).toBeInTheDocument();
    expect(within(await card('Prescripciones')).queryByRole('button', { name: 'Emitir prescripción' })).not.toBeInTheDocument();
    view.unmount();

    renderAs({ ...DOCTOR, permissions: DOCTOR.permissions.filter((p) => p !== 'MedicationAdministrations.Write') }, statefulBackend().routes);
    expect(await within(await card('Prescripciones')).findByRole('button', { name: 'Emitir prescripción' }, WAIT)).toBeInTheDocument();
    expect(within(await card('Administración de medicamentos (Kardex)')).queryByRole('button', { name: 'Registrar administración' })).not.toBeInTheDocument();
  });

  it('una atención cerrada no ofrece escrituras pero muestra el historial', async () => {
    const backend = statefulBackend('Closed');
    backend.prescriptions.push({
      id: 'rx-1', encounterId: ENC, status: 'Issued', notes: null, createdAtUtc: '2026-10-08T10:30:00Z', issuedAtUtc: '2026-10-08T10:30:00Z',
      details: [{ id: 'line-1', medicationId: PARACETAMOL.id, medicationName: 'Paracetamol', dose: 1, doseUnit: 'g', route: 'Oral', frequency: 'cada 8 horas', durationDays: null, instructions: null }],
    });
    renderAs(DOCTOR, backend.routes);

    expect(await within(await card('Prescripciones')).findByText(/1 g, Oral, cada 8 horas/, {}, WAIT)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Emitir prescripción' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Registrar administración' })).not.toBeInTheDocument();
  });

  it('muestra los errores 409, 400 y 403 del BFF', { timeout: 15000 }, async () => {
    const backend = statefulBackend();
    backend.prescriptions.push({
      id: 'rx-1', encounterId: ENC, status: 'Issued', notes: null, createdAtUtc: '2026-10-08T10:30:00Z', issuedAtUtc: '2026-10-08T10:30:00Z',
      details: [{ id: 'line-1', medicationId: PARACETAMOL.id, medicationName: 'Paracetamol', dose: 500, doseUnit: 'mg', route: 'Oral', frequency: 'cada 8 horas', durationDays: null, instructions: null }],
    });
    const { view } = renderAs(DOCTOR, {
      ...backend.routes,
      [`POST /api/web/encounters/${ENC}/prescriptions`]: () =>
        problem(409, 'The request conflicts with the current state.', { detail: 'An encounter in status Closed does not accept clinical records.' }),
      [`POST /api/web/encounters/${ENC}/medication-administrations`]: () =>
        problem(400, 'The request is not valid.', { detail: 'administeredAtUtc cannot be in the future.', field: 'administeredAtUtc' }),
    });
    const user = userEvent.setup();
    const prescriptionsCard = await card('Prescripciones');
    const medication = within(prescriptionsCard).getByLabelText(/Medicamento/);
    await waitFor(() => expect(medication).toBeEnabled(), WAIT);
    await user.selectOptions(medication, PARACETAMOL.id);
    await user.type(within(prescriptionsCard).getByLabelText(/^Dosis/), '1');
    await user.type(within(prescriptionsCard).getByLabelText(/Unidad/), 'g');
    await user.type(within(prescriptionsCard).getByLabelText(/Frecuencia/), 'cada 8 horas');
    await user.click(within(prescriptionsCard).getByRole('button', { name: 'Emitir prescripción' }));
    expect(await screen.findByText('La atención está cerrada o en un estado que no admite registros.', {}, WAIT)).toBeInTheDocument();

    const kardexCard = await card('Administración de medicamentos (Kardex)');
    await user.selectOptions(await within(kardexCard).findByLabelText(/Línea de tratamiento/, {}, WAIT), 'line-1');
    await user.click(within(kardexCard).getByRole('button', { name: 'Registrar administración' }));
    expect(await screen.findByText('La fecha y hora no pueden estar en el futuro.', {}, WAIT)).toBeInTheDocument();
    view.unmount();

    renderAs(DOCTOR, {
      ...statefulBackend().routes,
      [`GET /api/web/encounters/${ENC}/prescriptions`]: () =>
        problem(403, 'Access denied.', { detail: 'The resource belongs to a clinic the user is not affiliated with.' }),
    });
    expect(await screen.findByText(/No se pudieron cargar las prescripciones/, {}, WAIT)).toBeInTheDocument();
    expect(screen.getByText(/El registro pertenece a una clínica a la que no estás afiliado/)).toBeInTheDocument();
  });
});
