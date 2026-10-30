import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { AuthProvider, type WebUser } from '../../../auth';
import { ToastProvider } from '../../../components/shared';
import { AppRoutes } from '../../../routes';
import { createFakeBff, json, problem, session, type RecordedRequest } from '../../../test/fakeBff';
import { resetClinicSelectionForTests } from '../api/clinicSelection';
import type { EncounterAiSummary } from '../types/clinical.types';
import cardSource from '../components/EncounterAiSummaryCard.tsx?raw';

// Permisos reales de RolePermissions (backend).
const DOCTOR: WebUser = {
  id: '55555555-5555-5555-5555-555555555555',
  userName: 'medico.prueba',
  email: null,
  roles: ['Doctor'],
  permissions: ['Patients.Read', 'Encounters.Read', 'Medications.Read', 'Transfers.Read', 'Documents.Read', 'Encounters.Update', 'ClinicalNotes.Write', 'Diagnoses.Write'],
};

const ENC = 'e1';
const WAIT = { timeout: 5000 };
const SUMMARY_PATH = `/api/web/encounters/${ENC}/ai-summary`;
const ENCOUNTER = {
  id: ENC, patientId: 'p1', temporaryPatientCode: null, clinicId: 'c1', type: 'Emergency', status: 'InProgress', reason: null,
  startedAtUtc: '2026-10-08T10:00:00Z', endedAtUtc: null, latestTriagePriority: null,
  patient: { id: 'p1', displayName: 'Paciente Real', birthDate: null, biologicalSex: 'Female', identityStatus: 'Unverified', primaryIdentifier: null },
};

const SUMMARY: EncounterAiSummary = {
  aiGenerated: true,
  requiresClinicianReview: true,
  summary: 'Atención de tipo Emergency en estado InProgress. <b>no es HTML</b>',
  sections: {
    course: { text: 'Triaje con prioridad Urgent a +00:05.', sources: [{ type: 'Triage', id: 't-1' }] },
    vitalSigns: {
      text: 'PA sistólica cambió de 150 a 180 mm[Hg] entre +00:30 y +01:10 (2 registros).',
      sources: [{ type: 'VitalSign', id: 'v-1' }, { type: 'VitalSign', id: 'v-2' }],
    },
    medications: null,
    procedures: { text: 'Procedimiento Electrocardiograma en estado Completed a +00:20.', sources: [{ type: 'ClinicalProcedure', id: 'p-1' }] },
    notes: null,
    transfer: null,
  },
  reviewPoints: [
    { kind: 'PendingItem', text: 'Prescripción emitida a +00:50 sin administración documentada vinculada.', sources: [{ type: 'Prescription', id: 'rx-1' }] },
    { kind: 'MissingDocumentation', text: 'No hay notas clínicas documentadas en la atención.', sources: [] },
  ],
  notDocumented: ['Notas clínicas', 'Traslados'],
  generatedAtUtc: '2026-10-08T11:30:00Z',
  provider: 'SCIPH-AiFake',
  model: 'fake-summary-v1',
  promptVersion: 'encounter-summary.v1',
  sourceFingerprint: 'a'.repeat(64),
  truncated: false,
};

function backend(answer: (req: RecordedRequest, n: number) => Response | Promise<Response> = () => json(200, SUMMARY)) {
  return {
    'GET /api/web/clinical/context': () => json(200, { staffId: 's-1', clinics: [{ id: 'c1', name: 'Hospital' }] }),
    [`GET /api/web/encounters/${ENC}`]: () => json(200, ENCOUNTER),
    [`GET /api/web/encounters/${ENC}/diagnoses`]: () => json(200, []),
    [`GET /api/web/encounters/${ENC}/notes`]: () => json(200, []),
    [`GET /api/web/encounters/${ENC}/prescriptions`]: () => json(200, []),
    [`GET /api/web/encounters/${ENC}/medication-administrations`]: () => json(200, []),
    'GET /api/web/medications': () => json(200, []),
    [`POST ${SUMMARY_PATH}`]: answer,
  };
}

function renderAs(user: WebUser, routes: Record<string, (req: RecordedRequest, n: number) => Response | Promise<Response>>, path = `/hospital/atenciones/${ENC}`) {
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

const card = async () => (await screen.findByRole('heading', { name: 'Resumen asistido por IA' }, WAIT)).closest('section')!;

describe('resumen asistido por IA de la atención', () => {
  it('genera el resumen sin opciones y lo muestra como texto plano, con el aviso fijo y la hora', async () => {
    const { bff } = renderAs(DOCTOR, backend());
    const user = userEvent.setup();
    const panel = await card();
    expect(within(panel).queryByRole('note')).not.toBeInTheDocument();

    await user.click(within(panel).getByRole('button', { name: 'Generar resumen asistido' }));

    const result = await within(panel).findByRole('region', { name: 'Resumen generado por IA' }, WAIT);
    const request = bff.calls(`POST ${SUMMARY_PATH}`);
    expect(request).toHaveLength(1);
    expect(request[0].body).toBeUndefined();

    // Aviso fijo, sin botón para cerrarlo.
    expect(within(result).getByRole('note')).toHaveTextContent('Generado por IA · Requiere revisión profesional');
    expect(within(result).queryByRole('button', { name: /cerrar|ocultar|descartar/i })).not.toBeInTheDocument();

    // Texto plano: el HTML del contenido se ve literal y no crea elementos.
    expect(within(result).getByText(/<b>no es HTML<\/b>/)).toBeInTheDocument();
    expect(result.querySelector('b')).toBeNull();

    expect(within(result).getByRole('heading', { name: 'Evolución' })).toBeInTheDocument();
    expect(within(result).getByRole('heading', { name: 'Signos vitales' })).toBeInTheDocument();
    expect(within(result).getByRole('heading', { name: 'Procedimientos' })).toBeInTheDocument();
    expect(within(result).queryByRole('heading', { name: 'Medicación' })).not.toBeInTheDocument();
    expect(within(result).getByText(/PA sistólica cambió de 150 a 180/)).toBeInTheDocument();

    const points = within(result).getByRole('list', { name: 'Puntos a revisar' });
    expect(within(points).getAllByRole('listitem')).toHaveLength(2);
    expect(within(points).getByText('Elemento documentado pendiente')).toBeInTheDocument();
    expect(within(points).getByText('Documentación faltante')).toBeInTheDocument();
    expect(within(result).getByRole('list', { name: 'Datos no documentados' })).toHaveTextContent('Notas clínicas');

    // Registros relacionados: navegación a la pestaña donde se ven; sin pestaña, solo el tipo.
    expect(within(result).getByRole('link', { name: 'Signo vital (2)' })).toHaveAttribute('href', `/hospital/atenciones/${ENC}/triage`);
    expect(within(points).getByRole('link', { name: 'Prescripción (1)' })).toHaveAttribute('href', `/hospital/atenciones/${ENC}/medicacion`);
    expect(within(result).getByText('Procedimiento (1)').tagName).toBe('SPAN');

    expect(within(result).getByText(/Instantánea de los datos disponibles al momento de generación\. No se actualiza automáticamente\./)).toBeInTheDocument();
    expect(within(result).getByText(/Generado el/)).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Generar de nuevo' })).toBeEnabled();

    // Nunca lenguaje de autoridad clínica, ni nada guardado en el navegador.
    expect(panel.textContent).not.toMatch(/Diagnóstico IA|AI Diagnosis|Recomendaci|Recommended|Tratamiento recomendado|Gravedad|Severity|verificad/i);
    expect(sessionStorage.length + localStorage.length).toBe(0);
  });

  it('mientras genera, el botón queda deshabilitado y un doble clic no envía dos peticiones', async () => {
    let release: (response: Response) => void = () => {};
    const { bff } = renderAs(DOCTOR, backend(() => new Promise<Response>((resolve) => { release = resolve; })));
    const user = userEvent.setup();
    const panel = await card();
    const button = within(panel).getByRole('button', { name: 'Generar resumen asistido' });

    await user.dblClick(button);
    expect(await within(panel).findByRole('button', { name: /Generando resumen/ }, WAIT)).toBeDisabled();
    await user.click(within(panel).getByRole('button', { name: /Generando resumen/ }));
    expect(bff.calls(`POST ${SUMMARY_PATH}`)).toHaveLength(1);

    release(json(200, SUMMARY));
    expect(await within(panel).findByRole('region', { name: 'Resumen generado por IA' }, WAIT)).toBeInTheDocument();
    expect(bff.calls(`POST ${SUMMARY_PATH}`)).toHaveLength(1);
  });

  it.each([
    [problem(503, 'The service is temporarily unavailable.', { detail: 'The assisted summary is not available right now. Try again later.' }),
      'El resumen asistido no está disponible en este momento. Inténtalo más tarde.'],
    [json(429, { status: 429, title: 'Too many requests. Try again later.' }, { 'Retry-After': '30' }), /Demasiados intentos/],
    [problem(403, 'Access denied.', { detail: 'The current user cannot read this patient\'s encounters.' }), 'No tienes permiso para realizar esta acción.'],
    [problem(404, 'The resource was not found.', { detail: 'ClinicalEncounter e1 was not found.' }), 'El registro no existe o no es visible para tu usuario.'],
  ] as [Response, string | RegExp][])('un error se muestra con un mensaje seguro y sin contenido generado (%#)', async (response, message) => {
    renderAs(DOCTOR, backend(() => response.clone()));
    const user = userEvent.setup();
    const panel = await card();

    await user.click(within(panel).getByRole('button', { name: 'Generar resumen asistido' }));

    expect(await within(panel).findByRole('alert', {}, WAIT)).toHaveTextContent(message);
    expect(within(panel).queryByRole('region', { name: 'Resumen generado por IA' })).not.toBeInTheDocument();
    expect(panel.textContent).not.toMatch(/Exception|stack|prompt|SCIPH-Ai/i);
    expect(within(panel).getByRole('button', { name: 'Generar resumen asistido' })).toBeEnabled();
  });

  it('solo aparece en el expediente de la atención', async () => {
    renderAs(DOCTOR, backend(), `/hospital/atenciones/${ENC}/medicacion`);
    expect(await screen.findByRole('heading', { name: /Prescripciones/ }, WAIT)).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Resumen asistido por IA' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Generar resumen asistido' })).not.toBeInTheDocument();
  });

  it('el componente nunca inyecta HTML', () => {
    expect(cardSource).not.toMatch(/dangerouslySetInnerHTML|innerHTML/);
  });
});
