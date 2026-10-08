import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ApiError, resetApiClientForTests, setAccessToken } from '../../../api';
import { createFakeBff, json, problem } from '../../../test/fakeBff';
import { ClinicalApiError, describeError, isSimulated } from '../api';
import { ensureClinicalContext, resetClinicSelectionForTests, selectClinic } from '../api/clinicSelection';
import { httpClinicalApi } from '../api/httpClinicalApi';
import type { VitalSignInput } from '../types/clinical.types';

const ENCOUNTER = '7d3f2a10-0000-4000-8000-000000000001';
const CLINIC_A = 'aaaaaaaa-0000-4000-8000-000000000001';
const CLINIC_B = 'bbbbbbbb-0000-4000-8000-000000000002';
const fullUrl = (fetchMock: ReturnType<typeof createFakeBff>['fetchMock'], index = 0) => String(fetchMock.mock.calls[index][0]);

beforeEach(() => {
  resetApiClientForTests();
  resetClinicSelectionForTests();
  setAccessToken('test-access-token');
});

afterEach(() => {
  resetClinicSelectionForTests();
});

describe('httpClinicalApi (Web BFF real)', () => {
  it('busca pacientes por POST con el identificador en el cuerpo, nunca en la URL', async () => {
    const bff = createFakeBff({ 'POST /api/web/patients/search': () => json(200, []) });

    await httpClinicalApi.searchPatients({ identifierType: 'NationalId', value: '001-1234567-8' });

    const [request] = bff.calls('POST /api/web/patients/search');
    expect(request.body).toEqual({ identifierType: 'NationalId', value: '001-1234567-8' });
    expect(request.headers.Authorization).toBe('Bearer test-access-token');
    expect(fullUrl(bff.fetchMock)).not.toContain('1234567');
  });

  it('envía el triage con measurements y sin unit (la unidad la fija el backend)', async () => {
    const bff = createFakeBff({ [`POST /api/web/encounters/${ENCOUNTER}/triages`]: () => json(201, { id: 't1' }) });
    const withUnit = { type: 'HeartRate', value: 110, unit: '/min', measuredAtUtc: '2026-10-07T10:00:00.000Z' } as VitalSignInput;

    await httpClinicalApi.createTriage(ENCOUNTER, { priority: 'Urgent', reason: null, observations: null, measurements: [withUnit] });

    const body = bff.calls(`POST /api/web/encounters/${ENCOUNTER}/triages`)[0].body as Record<string, unknown>;
    expect(body.priority).toBe('Urgent');
    expect(body.measurements).toEqual([{ type: 'HeartRate', value: 110, measuredAtUtc: '2026-10-07T10:00:00.000Z' }]);
    expect(JSON.stringify(body)).not.toContain('unit');
    expect(body).not.toHaveProperty('vitalSigns');
  });

  it('registra un signo vital suelto sin unit', async () => {
    const bff = createFakeBff({ [`POST /api/web/encounters/${ENCOUNTER}/vital-signs`]: () => json(201, { id: 'v1', unit: 'Cel' }) });

    const saved = await httpClinicalApi.createVitalSign(ENCOUNTER, { type: 'Temperature', value: 38.2, measuredAtUtc: '2026-10-07T10:00:00.000Z' });

    expect(bff.calls(`POST /api/web/encounters/${ENCOUNTER}/vital-signs`)[0].body).toEqual({
      type: 'Temperature',
      value: 38.2,
      measuredAtUtc: '2026-10-07T10:00:00.000Z',
    });
    expect(saved.unit).toBe('Cel');
  });

  it('identifica una atención NN con el paciente en el cuerpo y usa la proyección de admisión', async () => {
    const bff = createFakeBff({
      [`POST /api/web/encounters/${ENCOUNTER}/identify`]: () => new Response(null, { status: 204 }),
      'GET /api/web/admissions/encounters': () => json(200, []),
    });

    await httpClinicalApi.identifyEncounterPatient(ENCOUNTER, 'patient-1');
    await httpClinicalApi.listAdmissionEncounters({ unidentifiedOnly: true });

    expect(bff.calls(`POST /api/web/encounters/${ENCOUNTER}/identify`)[0].body).toEqual({ patientId: 'patient-1' });
    expect(fullUrl(bff.fetchMock, 1)).toContain('unidentified=true');
  });

  it('solo envía clinicId cuando el usuario tiene varias clínicas', async () => {
    const bff = createFakeBff({
      'GET /api/web/encounters': () => json(200, []),
      'POST /api/web/encounters': () => json(201, { id: ENCOUNTER }),
    });

    ensureClinicalContext('user-1', async () => ({ staffId: 's1', clinics: [{ id: CLINIC_A, name: 'A' }] }));
    await Promise.resolve();
    await httpClinicalApi.listActiveEncounters();
    expect(fullUrl(bff.fetchMock, 0)).not.toContain('clinicId');

    resetClinicSelectionForTests();
    ensureClinicalContext('user-2', async () => ({
      staffId: 's2',
      clinics: [
        { id: CLINIC_A, name: 'A' },
        { id: CLINIC_B, name: 'B' },
      ],
    }));
    await Promise.resolve();
    selectClinic(CLINIC_B);
    await httpClinicalApi.listActiveEncounters();
    await httpClinicalApi.createEncounter({ type: 'Emergency', patientId: null, reason: null });

    expect(fullUrl(bff.fetchMock, 1)).toContain(`clinicId=${CLINIC_B}`);
    expect((bff.calls('POST /api/web/encounters')[0].body as { clinicId: string }).clinicId).toBe(CLINIC_B);
  });

  it('traduce los ProblemDetails clínicos conocidos y no inventa el egreso', async () => {
    createFakeBff({
      'GET /api/web/clinical/context': () => problem(403, 'Access denied.', { detail: 'The current user is not registered as clinical staff.' }),
    });

    const error = await httpClinicalApi.getClinicalContext().catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect(describeError(error)).toBe('Tu usuario no está registrado como personal clínico.');
    expect(describeError(new ApiError(400, { status: 400, title: 'x', detail: 'measuredAtUtc cannot be in the future.' }))).toBe(
      'La fecha y hora no pueden estar en el futuro.',
    );
    await expect(httpClinicalApi.closeEncounter(ENCOUNTER, { dischargeNote: null })).rejects.toBeInstanceOf(ClinicalApiError);
  });

  it('marca como simulado solo lo que aún no tiene backend', () => {
    for (const real of ['patients', 'encounters', 'admissions', 'triage', 'vitalSigns'] as const) expect(isSimulated(real)).toBe(false);
    for (const mock of ['notes', 'diagnoses', 'prescriptions', 'administrations', 'discharge', 'transfers', 'medications'] as const) {
      expect(isSimulated(mock)).toBe(true);
    }
  });
});
