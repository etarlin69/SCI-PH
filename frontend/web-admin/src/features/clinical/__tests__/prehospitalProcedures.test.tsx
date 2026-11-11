import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { AuthProvider, type WebUser } from '../../../auth';
import { ToastProvider } from '../../../components/shared';
import { AppRoutes } from '../../../routes';
import { createFakeBff, json, problem, session, type RecordedRequest } from '../../../test/fakeBff';
import { resetClinicSelectionForTests } from '../api/clinicSelection';
import type { ClinicalEncounterSummary, ClinicalProcedure } from '../types/clinical.types';

// Permisos reales de RolePermissions (backend).
const DOCTOR: WebUser = {
  id: '77777777-7777-7777-7777-777777777777',
  userName: 'medico.receptor',
  email: null,
  roles: ['Doctor'],
  permissions: ['Patients.Read', 'Encounters.Read', 'Medications.Read', 'Transfers.Read', 'Documents.Read', 'Patients.Identify', 'Encounters.Create', 'Encounters.Update', 'Encounters.Close', 'Triage.Write', 'VitalSigns.Write', 'ClinicalNotes.Write', 'Diagnoses.Write', 'Procedures.Write', 'Results.Write', 'Prescriptions.Write', 'MedicationAdministrations.Write', 'Transfers.Manage', 'Documents.Upload'],
};

const WAIT = { timeout: 5000 };
const ENCOUNTER_ID = 'enc-1';
const CLINIC_A = { id: 'clinic-a', name: 'Hospital A' };
const PROCEDURES = `GET /api/web/encounters/${ENCOUNTER_ID}/procedures`;

const encounter: ClinicalEncounterSummary = {
  id: ENCOUNTER_ID,
  patientId: null,
  patient: null,
  temporaryPatientCode: 'NN-20261109-ABCD',
  clinicId: CLINIC_A.id,
  type: 'Prehospital',
  status: 'Received',
  reason: 'Disnea',
  startedAtUtc: '2026-11-09T07:50:00Z',
  endedAtUtc: null,
  latestTriagePriority: null,
};

const procedure = (overrides: Partial<ClinicalProcedure>): ClinicalProcedure => ({
  id: 'p-1',
  encounterId: ENCOUNTER_ID,
  name: 'Oxigenoterapia',
  codeSystem: 'SCI-PH-PREHOSP',
  code: 'OXYGEN-THERAPY',
  status: 'Completed',
  performedAtUtc: '2026-11-09T08:00:00Z',
  completedAtUtc: '2026-11-09T08:00:00Z',
  outcome: 'SpO2 97 %',
  performedByDisplayName: 'Carlos Paramédico',
  prehospital: true,
  ...overrides,
});

function renderAs(user: WebUser, procedures: (req: RecordedRequest, n: number) => Response) {
  resetClinicSelectionForTests();
  const bff = createFakeBff({
    'POST /api/web/auth/refresh': () => json(200, session(user)),
    'GET /api/web/auth/me': () => json(200, user),
    'GET /api/web/clinical/context': () => json(200, { staffId: 's-a', clinics: [CLINIC_A] }),
    [`GET /api/web/encounters/${ENCOUNTER_ID}`]: () => json(200, encounter),
    [`GET /api/web/encounters/${ENCOUNTER_ID}/notes`]: () => json(200, []),
    [`GET /api/web/encounters/${ENCOUNTER_ID}/diagnoses`]: () => json(200, []),
    [`GET /api/web/encounters/${ENCOUNTER_ID}/transfers`]: () => json(200, []),
    [PROCEDURES]: procedures,
  });
  render(
    <ToastProvider>
      <AuthProvider>
        <MemoryRouter initialEntries={[`/hospital/atenciones/${ENCOUNTER_ID}`]}>
          <AppRoutes />
        </MemoryRouter>
      </AuthProvider>
    </ToastProvider>,
  );
  return bff;
}

const card = async () => (await screen.findByRole('heading', { name: 'Procedimientos', level: 2 }, WAIT)).closest('section')!;

describe('procedimientos prehospitalarios en el expediente', () => {
  it('muestra los procedimientos de la ambulancia en la misma atención, marcados como prehospitalarios', async () => {
    const bff = renderAs(DOCTOR, () =>
      json(200, [
        procedure({ id: 'p-2', name: 'Acceso venoso periférico', code: 'PERIPHERAL-IV-ACCESS', outcome: null, performedAtUtc: '2026-11-09T08:10:00Z' }),
        procedure({}),
      ]),
    );

    const section = await card();
    expect(await within(section).findByText('Acceso venoso periférico', {}, WAIT)).toBeInTheDocument();
    expect(within(section).getByText('Oxigenoterapia')).toBeInTheDocument();
    expect(within(section).getAllByText('Prehospitalario')).toHaveLength(2);
    expect(within(section).getAllByText(/Carlos Paramédico/)).toHaveLength(2);
    expect(within(section).getByText('SpO2 97 %')).toBeInTheDocument();
    // Solo lectura: la web no registra procedimientos.
    expect(within(section).queryByRole('button')).not.toBeInTheDocument();
    expect(bff.calls(PROCEDURES)).toHaveLength(1);
    expect(bff.requests.filter((r) => r.path.endsWith('/procedures')).every((r) => r.method === 'GET')).toBe(true);
  });

  it('distingue un procedimiento del hospital y muestra su estado si no está completado', async () => {
    renderAs(DOCTOR, () =>
      json(200, [procedure({ id: 'p-3', name: 'Sutura', codeSystem: null, code: null, prehospital: false, status: 'InProgress', outcome: null, performedByDisplayName: null })]),
    );

    const section = await card();
    expect(await within(section).findByText('Sutura', {}, WAIT)).toBeInTheDocument();
    expect(within(section).queryByText('Prehospitalario')).not.toBeInTheDocument();
    expect(within(section).getByText('En curso')).toBeInTheDocument();
  });

  it('sin procedimientos muestra el estado vacío', async () => {
    renderAs(DOCTOR, () => json(200, []));

    const section = await card();
    expect(await within(section).findByText('Sin procedimientos registrados', {}, WAIT)).toBeInTheDocument();
  });

  it('un error muestra el mensaje y se puede reintentar', async () => {
    const bff = renderAs(DOCTOR, (_req, n) => (n === 0 ? problem(500, 'Server error') : json(200, [procedure({})])));
    const user = userEvent.setup();

    const section = await card();
    expect(await within(section).findByText(/No se pudieron cargar los procedimientos/, {}, WAIT)).toBeInTheDocument();
    expect(within(section).queryByText('Oxigenoterapia')).not.toBeInTheDocument();
    await user.click(within(section).getByRole('button', { name: /Reintentar/ }));
    expect(await within(section).findByText('Oxigenoterapia', {}, WAIT)).toBeInTheDocument();
    expect(bff.calls(PROCEDURES)).toHaveLength(2);
  });
});
