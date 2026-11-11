import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { AuthProvider, type WebUser } from '../../../auth';
import { ToastProvider } from '../../../components/shared';
import { AppRoutes } from '../../../routes';
import { createFakeBff, json, problem, session, type RecordedRequest } from '../../../test/fakeBff';
import { resetClinicSelectionForTests } from '../api/clinicSelection';
import type { ClinicalEncounterStatus, ClinicalEncounterSummary } from '../types/clinical.types';
import type { ClinicalResult } from '../types/results.types';

// Permisos reales de RolePermissions (backend).
const DOCTOR: WebUser = {
  id: '77777777-7777-7777-7777-777777777777',
  userName: 'medico.prueba',
  email: null,
  roles: ['Doctor'],
  permissions: ['Patients.Read', 'Encounters.Read', 'Medications.Read', 'Transfers.Read', 'Documents.Read', 'Patients.Identify', 'Encounters.Create', 'Encounters.Update', 'Encounters.Close', 'Triage.Write', 'VitalSigns.Write', 'ClinicalNotes.Write', 'Diagnoses.Write', 'Procedures.Write', 'Results.Write', 'Prescriptions.Write', 'MedicationAdministrations.Write', 'Transfers.Manage', 'Documents.Upload'],
};
const NURSE: WebUser = {
  id: '88888888-8888-8888-8888-888888888888',
  userName: 'enfermeria.prueba',
  email: null,
  roles: ['Nurse'],
  permissions: ['Patients.Read', 'Encounters.Read', 'Medications.Read', 'Transfers.Read', 'Documents.Read', 'Encounters.Update', 'Triage.Write', 'VitalSigns.Write', 'ClinicalNotes.Write', 'Procedures.Write', 'MedicationAdministrations.Write', 'Documents.Upload'],
};

const WAIT = { timeout: 5000 };
const ENCOUNTER_ID = 'enc-1';
const CLINIC_A = { id: 'clinic-a', name: 'Hospital A' };
const LIST = `GET /api/web/encounters/${ENCOUNTER_ID}/results`;

const encounter = (status: ClinicalEncounterStatus): ClinicalEncounterSummary => ({
  id: ENCOUNTER_ID,
  patientId: 'p-1',
  patient: null,
  temporaryPatientCode: null,
  clinicId: CLINIC_A.id,
  type: 'Emergency',
  status,
  reason: 'Fiebre',
  startedAtUtc: '2026-11-09T07:50:00Z',
  endedAtUtc: null,
  latestTriagePriority: null,
});

const result = (overrides: Partial<ClinicalResult>): ClinicalResult => ({
  id: 'r-1',
  encounterId: ENCOUNTER_ID,
  type: 'Laboratory',
  status: 'Pending',
  name: 'Hemoglobina',
  codeSystem: null,
  code: null,
  resultText: null,
  numericValue: null,
  unit: null,
  referenceRange: '12-16 g/dL',
  resultedAtUtc: null,
  recordedByDisplayName: null,
  ...overrides,
});

function renderAs(user: WebUser, status: ClinicalEncounterStatus, routes: Record<string, (req: RecordedRequest, n: number) => Response>) {
  resetClinicSelectionForTests();
  const bff = createFakeBff({
    'POST /api/web/auth/refresh': () => json(200, session(user)),
    'GET /api/web/auth/me': () => json(200, user),
    'GET /api/web/clinical/context': () => json(200, { staffId: 's-a', clinics: [CLINIC_A] }),
    [`GET /api/web/encounters/${ENCOUNTER_ID}`]: () => json(200, encounter(status)),
    [`GET /api/web/encounters/${ENCOUNTER_ID}/notes`]: () => json(200, []),
    [`GET /api/web/encounters/${ENCOUNTER_ID}/diagnoses`]: () => json(200, []),
    [`GET /api/web/encounters/${ENCOUNTER_ID}/procedures`]: () => json(200, []),
    [`GET /api/web/encounters/${ENCOUNTER_ID}/transfers`]: () => json(200, []),
    ...routes,
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

const card = async () => (await screen.findByRole('heading', { name: 'Resultados', level: 2 }, WAIT)).closest('section')!;

describe('resultados de la atención', () => {
  it('pide una orden y luego registra su valor', async () => {
    let stored: ClinicalResult[] = [];
    const bff = renderAs(DOCTOR, 'InProgress', {
      [LIST]: () => json(200, stored),
      [`POST /api/web/encounters/${ENCOUNTER_ID}/results`]: () => {
        stored = [result({})];
        return json(201, stored[0]);
      },
      [`POST /api/web/encounters/${ENCOUNTER_ID}/results/r-1/final`]: () => {
        stored = [result({ status: 'Final', numericValue: 13.5, unit: 'g/dL', resultedAtUtc: '2026-11-09T09:00:00Z', recordedByDisplayName: 'Dra. Prueba' })];
        return json(200, stored[0]);
      },
    });
    const user = userEvent.setup();

    const section = await card();
    expect(await within(section).findByText('Sin resultados', {}, WAIT)).toBeInTheDocument();
    await user.selectOptions(within(section).getByLabelText(/^Tipo/), 'Laboratory');
    await user.type(within(section).getByLabelText(/Estudio/), 'Hemoglobina');
    await user.type(within(section).getByLabelText(/Rango de referencia/), '12-16 g/dL');
    await user.click(within(section).getByRole('button', { name: 'Solicitar' }));

    expect(await within(section).findByText('Pendiente', {}, WAIT)).toBeInTheDocument();
    expect(bff.calls(`POST /api/web/encounters/${ENCOUNTER_ID}/results`)[0].body).toEqual({ type: 'Laboratory', name: 'Hemoglobina', referenceRange: '12-16 g/dL' });

    await user.click(within(section).getByRole('button', { name: 'Registrar resultado' }));
    const form = within(section).getByRole('form', { name: 'Valor de Hemoglobina' });
    await user.type(within(form).getByLabelText(/Valor numérico/), '13,5');
    await user.type(within(form).getByLabelText(/Unidad/), 'g/dL');
    await user.click(within(form).getByRole('button', { name: 'Guardar' }));

    expect(await within(section).findByText('13.5 g/dL', {}, WAIT)).toBeInTheDocument();
    expect(within(section).getByText('Final')).toBeInTheDocument();
    expect(bff.calls(`POST /api/web/encounters/${ENCOUNTER_ID}/results/r-1/final`)[0].body).toEqual({ resultText: null, numericValue: 13.5, unit: 'g/dL' });
    expect(within(section).getByRole('button', { name: 'Corregir' })).toBeInTheDocument();
  }, 20000);

  it('sin valor no envía nada y un 409 se muestra junto al formulario', async () => {
    const bff = renderAs(DOCTOR, 'InProgress', {
      [LIST]: () => json(200, [result({})]),
      [`POST /api/web/encounters/${ENCOUNTER_ID}/results/r-1/final`]: () => problem(409, 'Conflict', { detail: 'The result was changed by another operation. Reload it and try again.' }),
    });
    const user = userEvent.setup();

    const section = await card();
    await user.click(await within(section).findByRole('button', { name: 'Registrar resultado' }, WAIT));
    const form = within(section).getByRole('form', { name: 'Valor de Hemoglobina' });
    await user.click(within(form).getByRole('button', { name: 'Guardar' }));
    expect(within(form).getByRole('alert')).toHaveTextContent('Indica un valor numérico o un texto.');
    expect(bff.calls(`POST /api/web/encounters/${ENCOUNTER_ID}/results/r-1/final`)).toHaveLength(0);

    await user.type(within(form).getByLabelText(/Resultado \(texto\)/), 'Normal');
    await user.click(within(form).getByRole('button', { name: 'Guardar' }));
    expect(await within(form).findByRole('alert', {}, WAIT)).toBeInTheDocument();
    expect(bff.calls(`POST /api/web/encounters/${ENCOUNTER_ID}/results/r-1/final`)).toHaveLength(1);
  }, 20000);

  it('enfermería los lee pero no los cambia', async () => {
    renderAs(NURSE, 'InProgress', { [LIST]: () => json(200, [result({ status: 'Final', resultText: 'Normal' })]) });

    const section = await card();
    expect(await within(section).findByText('Normal', {}, WAIT)).toBeInTheDocument();
    expect(within(section).queryByRole('button')).not.toBeInTheDocument();
    expect(within(section).queryByRole('heading', { name: 'Solicitar resultado' })).not.toBeInTheDocument();
  });

  it('una atención cerrada es solo lectura', async () => {
    renderAs(DOCTOR, 'Closed', { [LIST]: () => json(200, [result({})]) });

    const section = await card();
    expect(await within(section).findByText('Hemoglobina', {}, WAIT)).toBeInTheDocument();
    expect(within(section).queryByRole('button', { name: 'Registrar resultado' })).not.toBeInTheDocument();
    expect(within(section).queryByRole('button', { name: 'Solicitar' })).not.toBeInTheDocument();
  });
});
