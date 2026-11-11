import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider, type WebUser } from '../../../auth';
import { ToastProvider } from '../../../components/shared';
import { AppRoutes } from '../../../routes';
import { createFakeBff, json, problem, session, type RecordedRequest } from '../../../test/fakeBff';
import { resetClinicSelectionForTests } from '../api/clinicSelection';
import type { ClinicalEncounterStatus, ClinicalEncounterSummary } from '../types/clinical.types';
import type { ClinicalDocument } from '../types/documents.types';

// Permisos reales de RolePermissions (backend).
const DOCTOR: WebUser = {
  id: '77777777-7777-7777-7777-777777777777',
  userName: 'medico.prueba',
  email: null,
  roles: ['Doctor'],
  permissions: ['Patients.Read', 'Encounters.Read', 'Medications.Read', 'Transfers.Read', 'Documents.Read', 'Patients.Identify', 'Encounters.Create', 'Encounters.Update', 'Encounters.Close', 'Triage.Write', 'VitalSigns.Write', 'ClinicalNotes.Write', 'Diagnoses.Write', 'Procedures.Write', 'Results.Write', 'Prescriptions.Write', 'MedicationAdministrations.Write', 'Transfers.Manage', 'Documents.Upload'],
};

const WAIT = { timeout: 5000 };
const ENCOUNTER_ID = 'enc-1';
const CLINIC_A = { id: 'clinic-a', name: 'Hospital A' };
const LIST = `GET /api/web/encounters/${ENCOUNTER_ID}/documents`;
const UPLOAD = `POST /api/web/encounters/${ENCOUNTER_ID}/documents`;

const encounter = (status: ClinicalEncounterStatus): ClinicalEncounterSummary => ({
  id: ENCOUNTER_ID,
  patientId: 'p-1',
  patient: null,
  temporaryPatientCode: null,
  clinicId: CLINIC_A.id,
  type: 'Emergency',
  status,
  reason: 'Dolor torácico',
  startedAtUtc: '2026-11-09T07:50:00Z',
  endedAtUtc: null,
  latestTriagePriority: null,
});

const stored: ClinicalDocument = {
  id: 'doc-1',
  encounterId: ENCOUNTER_ID,
  type: 'LabResult',
  fileName: 'hemograma.pdf',
  contentType: 'application/pdf',
  sizeBytes: 2048,
  uploadedAtUtc: '2026-11-09T08:00:00Z',
};

function renderAs(user: WebUser, status: ClinicalEncounterStatus, routes: Record<string, (req: RecordedRequest, n: number) => Response>) {
  resetClinicSelectionForTests();
  const bff = createFakeBff({
    'POST /api/web/auth/refresh': () => json(200, session(user)),
    'GET /api/web/auth/me': () => json(200, user),
    'GET /api/web/clinical/context': () => json(200, { staffId: 's-a', clinics: [CLINIC_A] }),
    [`GET /api/web/encounters/${ENCOUNTER_ID}`]: () => json(200, encounter(status)),
    [`GET /api/web/encounters/${ENCOUNTER_ID}/notes`]: () => json(200, []),
    [`GET /api/web/encounters/${ENCOUNTER_ID}/diagnoses`]: () => json(200, []),
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

const card = async () => (await screen.findByRole('heading', { name: 'Documentos', level: 2 }, WAIT)).closest('section')!;
const pdf = (size = 4) => new File([new Uint8Array(size)], 'informe.pdf', { type: 'application/pdf' });

afterEach(() => vi.restoreAllMocks());

describe('documentos de la atención', () => {
  it('lista los documentos y descarga el contenido con la sesión, sin guardarlo', async () => {
    const created = vi.fn(() => 'blob:sciph-test');
    const revoked = vi.fn();
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: created, revokeObjectURL: revoked }));
    const clicked = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
    const bff = renderAs(DOCTOR, 'InProgress', {
      [LIST]: () => json(200, [stored]),
      [`GET /api/web/encounters/${ENCOUNTER_ID}/documents/doc-1/content`]: () =>
        new Response(new Blob(['%PDF-1.4'], { type: 'application/pdf' }), { status: 200, headers: { 'Content-Type': 'application/pdf' } }),
    });
    const user = userEvent.setup();

    const section = await card();
    expect(await within(section).findByText('hemograma.pdf', {}, WAIT)).toBeInTheDocument();
    expect(within(section).getByText(/Resultado de laboratorio · 2 KB/)).toBeInTheDocument();
    await user.click(within(section).getByRole('button', { name: 'Descargar hemograma.pdf' }));

    await waitFor(() => expect(revoked).toHaveBeenCalledWith('blob:sciph-test'), WAIT);
    expect(clicked).toHaveBeenCalledOnce();
    const download = bff.calls(`GET /api/web/encounters/${ENCOUNTER_ID}/documents/doc-1/content`)[0];
    expect(download.headers.Authorization).toMatch(/^Bearer /);
  });

  it('adjunta un archivo con multipart (file, type, clientRequestId) y conserva la clave al reintentar', async () => {
    const bff = renderAs(DOCTOR, 'InProgress', {
      [LIST]: (_req, n) => json(200, n === 0 ? [] : [stored]),
      [UPLOAD]: (_req, n) => (n === 0 ? problem(503, 'Service unavailable') : json(201, stored)),
    });
    const user = userEvent.setup();

    const section = await card();
    expect(await within(section).findByText('Sin documentos adjuntos', {}, WAIT)).toBeInTheDocument();
    await user.selectOptions(within(section).getByLabelText(/Tipo de documento/), 'LabResult');
    await user.upload(within(section).getByLabelText(/Archivo/), pdf());
    await user.click(within(section).getByRole('button', { name: 'Adjuntar' }));

    // El error se muestra junto al formulario; el reintento lleva la MISMA clave.
    await waitFor(() => expect(bff.requests.map((r) => `${r.method} ${r.path}`)).toContain(UPLOAD), WAIT);
    expect(await within(section).findByRole('alert', {}, WAIT)).toBeInTheDocument();
    await user.click(within(section).getByRole('button', { name: 'Adjuntar' }));
    expect(await within(section).findByText('hemograma.pdf', {}, WAIT)).toBeInTheDocument();

    const uploads = bff.calls(UPLOAD);
    expect(uploads).toHaveLength(2);
    const [first, second] = uploads.map((r) => r.body as FormData);
    expect([...first.keys()].sort()).toEqual(['clientRequestId', 'file', 'type']);
    expect(first.get('type')).toBe('LabResult');
    expect((first.get('file') as File).name).toBe('informe.pdf');
    expect(second.get('clientRequestId')).toBe(first.get('clientRequestId'));
  }, 20000);

  it('un archivo de más de 10 MB no se envía', async () => {
    const bff = renderAs(DOCTOR, 'InProgress', { [LIST]: () => json(200, []) });
    const user = userEvent.setup();

    const section = await card();
    await user.upload(within(section).getByLabelText(/Archivo/), pdf(10 * 1024 * 1024 + 1));

    expect(within(section).getByRole('alert')).toHaveTextContent('El archivo supera los 10 MB.');
    expect(within(section).getByRole('button', { name: 'Adjuntar' })).toBeDisabled();
    expect(bff.calls(UPLOAD)).toHaveLength(0);
  });

  it('una atención cerrada muestra sus documentos pero no permite adjuntar', async () => {
    renderAs(DOCTOR, 'Closed', { [LIST]: () => json(200, [stored]) });

    const section = await card();
    expect(await within(section).findByText('hemograma.pdf', {}, WAIT)).toBeInTheDocument();
    expect(within(section).queryByRole('button', { name: 'Adjuntar' })).not.toBeInTheDocument();
  });

  it('sin Documents.Read no se muestra la tarjeta', async () => {
    const bff = renderAs({ ...DOCTOR, permissions: DOCTOR.permissions.filter((p) => p !== 'Documents.Read') }, 'InProgress', {});

    expect(await screen.findByRole('heading', { name: 'Notas clínicas' }, WAIT)).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Documentos', level: 2 })).not.toBeInTheDocument();
    expect(bff.calls(LIST)).toHaveLength(0);
  });
});
