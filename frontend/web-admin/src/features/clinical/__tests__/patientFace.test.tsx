import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider, type WebUser } from '../../../auth';
import { ToastProvider } from '../../../components/shared';
import { AppRoutes } from '../../../routes';
import { createFakeBff, json, problem, session, type RecordedRequest } from '../../../test/fakeBff';
import { resetClinicSelectionForTests } from '../api/clinicSelection';
import type { FaceVerificationOutcome, PatientFace, PatientSummary } from '../types/clinical.types';

// Permisos reales de RolePermissions (backend).
const RECEPTIONIST: WebUser = {
  id: '44444444-4444-4444-4444-444444444444',
  userName: 'recepcion',
  email: null,
  roles: ['Receptionist'],
  permissions: ['Patients.Read', 'Patients.Create', 'Patients.Update', 'Patients.Identify', 'Biometrics.Manage', 'Accounts.InvitePatient', 'Encounters.Create'],
};
const DOCTOR: WebUser = {
  id: '55555555-5555-5555-5555-555555555555',
  userName: 'medico',
  email: null,
  roles: ['Doctor'],
  permissions: ['Patients.Read', 'Encounters.Read', 'Patients.Identify'],
};
const NURSE: WebUser = {
  id: '66666666-6666-6666-6666-666666666666',
  userName: 'enfermeria',
  email: null,
  roles: ['Nurse'],
  permissions: ['Patients.Read', 'Encounters.Read'],
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
const ACTIVE: PatientFace = { id: 'face-1', status: 'Active', provider: 'SCIPH-FaceFake', registeredAtUtc: '2030-01-01T00:01:00Z', revokedAtUtc: null };
const REVOKED: PatientFace = { ...ACTIVE, status: 'Revoked', revokedAtUtc: '2030-01-02T00:00:00Z' };

// Cámara simulada: un stream con una pista cuyo stop() se puede comprobar.
let track: { stop: ReturnType<typeof vi.fn> };
let getUserMedia: ReturnType<typeof vi.fn>;
const revokeObjectURL = vi.fn();

beforeEach(() => {
  track = { stop: vi.fn() };
  getUserMedia = vi.fn().mockResolvedValue({ getTracks: () => [track] });
  Object.defineProperty(navigator, 'mediaDevices', { configurable: true, value: { getUserMedia } });
  vi.spyOn(HTMLMediaElement.prototype, 'play').mockResolvedValue(undefined);
  HTMLCanvasElement.prototype.getContext = vi.fn(() => null) as unknown as HTMLCanvasElement['getContext'];
  HTMLCanvasElement.prototype.toBlob = function (callback: (blob: Blob | null) => void) {
    callback(new Blob(['synthetic'], { type: 'image/jpeg' }));
  };
  URL.createObjectURL = vi.fn(() => 'blob:preview-1');
  URL.revokeObjectURL = revokeObjectURL;
});

afterEach(() => {
  revokeObjectURL.mockClear();
  vi.restoreAllMocks();
});

// Backend con estado: la lista refleja lo registrado y revocado.
function backend(initial: PatientFace[], answers: { verify?: () => Response; enroll?: () => Response } = {}) {
  let faces = initial;
  const routes: Record<string, (req: RecordedRequest, n: number) => Response> = {
    'GET /api/web/clinical/context': () => json(200, { staffId: 's-1', clinics: [{ id: 'clinic-1', name: 'Hospital' }] }),
    'GET /api/web/patients/p-1': () => json(200, PATIENT),
    'GET /api/web/patients/p-1/fingerprints': () => json(200, []),
    'GET /api/web/fingerprint-enrollers': () => json(200, []),
    'GET /api/web/patients/p-1/faces': () => json(200, faces),
    'POST /api/web/patients/p-1/faces/enroll': () => {
      const response = answers.enroll?.() ?? json(201, { outcome: 'Enrolled', face: ACTIVE });
      if (response.status === 201) faces = [ACTIVE, ...faces];
      return response;
    },
    'POST /api/web/patients/p-1/faces/verify': () => answers.verify?.() ?? json(200, { verificationId: 'v-1', outcome: 'Match' }),
    'POST /api/web/patients/p-1/faces/face-1/revoke': () => {
      faces = faces.map((f) => (f.id === 'face-1' ? REVOKED : f));
      return json(200, REVOKED);
    },
  };
  return routes;
}

function renderAs(user: WebUser, routes: Record<string, (req: RecordedRequest, n: number) => Response>) {
  resetClinicSelectionForTests();
  const bff = createFakeBff({
    'POST /api/web/auth/refresh': () => json(200, session(user)),
    'GET /api/web/auth/me': () => json(200, user),
    ...routes,
  });
  const view = render(
    <ToastProvider>
      <AuthProvider>
        <MemoryRouter initialEntries={['/hospital/pacientes/p-1']}>
          <AppRoutes />
        </MemoryRouter>
      </AuthProvider>
    </ToastProvider>,
  );
  return { bff, view };
}

const card = async () => (await screen.findByRole('heading', { name: 'Rostro' }, WAIT)).closest('section')!;

async function capture(button: string, confirm: string) {
  const user = userEvent.setup();
  const face = await card();
  await user.click(await within(face).findByRole('button', { name: button }, WAIT));
  await user.click(await within(face).findByRole('button', { name: 'Tomar foto' }, WAIT));
  await user.click(await within(face).findByRole('button', { name: confirm }, WAIT));
  return { user, face };
}

describe('rostro del paciente', () => {
  it('registra un rostro con la cámara: solo la captura y su clave; la cámara se apaga y la foto no queda', async () => {
    const { bff } = renderAs(RECEPTIONIST, backend([]));
    const face = await card();
    expect(await within(face).findByText('Sin rostro registrado', {}, WAIT)).toBeInTheDocument();
    expect(within(face).getByText(/Verificación asistida: ayuda al personal presente\. No es una autenticación/)).toBeInTheDocument();
    expect(face.querySelector('input[type="file"]')).toBeNull();

    await capture('Registrar rostro', 'Registrar este rostro');

    expect(getUserMedia).toHaveBeenCalledWith(expect.objectContaining({ audio: false }));
    expect(track.stop).toHaveBeenCalled();
    await waitFor(() => expect(bff.calls('POST /api/web/patients/p-1/faces/enroll')).toHaveLength(1));
    const form = bff.calls('POST /api/web/patients/p-1/faces/enroll')[0].body as FormData;
    expect([...form.keys()].sort()).toEqual(['capture', 'clientRequestId']);
    expect(form.get('capture')).toBeInstanceOf(Blob);
    expect(await within(face).findByText('Rostro registrado.', {}, WAIT)).toBeInTheDocument();
    expect(await within(face).findByText('Activo', {}, WAIT)).toBeInTheDocument();
    expect(within(face).getByText('Proveedor simulado (desarrollo)')).toBeInTheDocument();
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:preview-1');
    expect(face.textContent).not.toMatch(/face:|subjectReference|score|threshold|umbral|%/i);
    expect(sessionStorage.length + localStorage.length).toBe(0);
  });

  it.each([
    ['Match', 'Coincide'],
    ['NoMatch', 'No coincide'],
    ['NoFace', 'No se detectó un rostro'],
    ['MultipleFaces', 'Se detectaron varios rostros'],
    ['LowQuality', 'Calidad insuficiente'],
  ] as [FaceVerificationOutcome, string][])('verificar muestra solo el resultado (%s), nunca un puntaje', async (outcome, text) => {
    const { bff } = renderAs(RECEPTIONIST, backend([ACTIVE], { verify: () => json(200, { verificationId: 'v-1', outcome }) }));
    const { face } = await capture('Verificar rostro', 'Verificar este rostro');

    const result = await within(face).findByRole('status', { name: 'Resultado de la verificación' }, WAIT);
    expect(result).toHaveTextContent(text);
    const form = bff.calls('POST /api/web/patients/p-1/faces/verify')[0].body as FormData;
    expect([...form.keys()].sort()).toEqual(['capture', 'clientRequestId']);
    expect(face.textContent).not.toMatch(/score|confianza|threshold|umbral|%|similar/i);
    expect(track.stop).toHaveBeenCalled();
  });

  it('un proveedor no disponible se muestra con un mensaje seguro', async () => {
    renderAs(
      RECEPTIONIST,
      backend([ACTIVE], { verify: () => problem(503, 'The service is temporarily unavailable.', { detail: 'The face verification service is not available. Try again later.' }) }),
    );
    const { face } = await capture('Verificar rostro', 'Verificar este rostro');

    expect(await within(face).findByText('La verificación facial no está disponible en este momento. Inténtalo más tarde.', {}, WAIT)).toBeInTheDocument();
  });

  it('un registro sin rostro usable lo dice y permite repetir la foto', async () => {
    renderAs(RECEPTIONIST, backend([], { enroll: () => json(200, { outcome: 'NoFace', face: null }) }));
    const { face } = await capture('Registrar rostro', 'Registrar este rostro');

    expect(await within(face).findByText('No se detectó un rostro. Toma la foto de nuevo.', {}, WAIT)).toBeInTheDocument();
    expect(within(face).getByRole('button', { name: 'Repetir' })).toBeInTheDocument();
    expect(within(face).getByText('Sin rostro registrado')).toBeInTheDocument();
  });

  it('revoca con confirmación y motivo opcional, y luego permite registrar otro rostro', async () => {
    const { bff } = renderAs(RECEPTIONIST, backend([ACTIVE]));
    const user = userEvent.setup();
    const face = await card();

    await user.click(await within(face).findByRole('button', { name: 'Revocar rostro' }, WAIT));
    const dialog = await screen.findByRole('alertdialog', {}, WAIT);
    expect(within(dialog).getByText(/dejará de usarse para verificar/)).toBeInTheDocument();
    await user.selectOptions(within(dialog).getByRole('combobox', { name: /Motivo/ }), 'ReferenceOutdated');
    await user.click(within(dialog).getByRole('button', { name: 'Revocar' }));

    await waitFor(() => expect(bff.calls('POST /api/web/patients/p-1/faces/face-1/revoke')).toHaveLength(1));
    expect(bff.calls('POST /api/web/patients/p-1/faces/face-1/revoke')[0].body).toEqual({ reason: 'ReferenceOutdated' });
    expect(await within(face).findByText('Revocado', {}, WAIT)).toBeInTheDocument();
    expect(within(face).queryByRole('button', { name: 'Verificar rostro' })).not.toBeInTheDocument();
    expect(await within(face).findByRole('button', { name: 'Registrar rostro' }, WAIT)).toBeInTheDocument();
  });

  it('sin permiso de cámara o sin cámara lo explica y deja cancelar', async () => {
    getUserMedia.mockRejectedValueOnce(new DOMException('denied', 'NotAllowedError'));
    renderAs(RECEPTIONIST, backend([]));
    const user = userEvent.setup();
    const face = await card();

    await user.click(await within(face).findByRole('button', { name: 'Registrar rostro' }, WAIT));
    expect(await within(face).findByText(/el permiso fue denegado/, {}, WAIT)).toBeInTheDocument();
    await user.click(within(face).getByRole('button', { name: 'Cancelar' }));

    Object.defineProperty(navigator, 'mediaDevices', { configurable: true, value: undefined });
    await user.click(await within(face).findByRole('button', { name: 'Registrar rostro' }, WAIT));
    expect(await within(face).findByText('No hay una cámara disponible en este equipo.', {}, WAIT)).toBeInTheDocument();
  });

  it('cancelar o salir de la pantalla apaga la cámara', async () => {
    const { view } = renderAs(RECEPTIONIST, backend([ACTIVE]));
    const user = userEvent.setup();
    const face = await card();

    await user.click(await within(face).findByRole('button', { name: 'Verificar rostro' }, WAIT));
    await within(face).findByRole('button', { name: 'Tomar foto' }, WAIT);
    await user.click(within(face).getByRole('button', { name: 'Cancelar' }));
    expect(track.stop).toHaveBeenCalledTimes(1);

    await user.click(await within(face).findByRole('button', { name: 'Verificar rostro' }, WAIT));
    await within(face).findByRole('button', { name: 'Tomar foto' }, WAIT);
    view.unmount();
    expect(track.stop).toHaveBeenCalledTimes(2);
  });

  it('Patients.Identify sin Biometrics.Manage verifica pero no registra ni revoca; sin ninguno no hay tarjeta', async () => {
    renderAs(DOCTOR, backend([ACTIVE]));
    const face = await card();
    expect(await within(face).findByRole('button', { name: 'Verificar rostro' }, WAIT)).toBeInTheDocument();
    expect(within(face).queryByRole('button', { name: 'Revocar rostro' })).not.toBeInTheDocument();
    expect(within(face).queryByRole('button', { name: 'Registrar rostro' })).not.toBeInTheDocument();
  });

  it('el proveedor local real se muestra con su etiqueta', async () => {
    renderAs(DOCTOR, backend([{ ...ACTIVE, provider: 'SCIPH-FaceOpenCV' }]));
    const face = await card();
    expect(await within(face).findByText('Reconocimiento local (OpenCV)', {}, WAIT)).toBeInTheDocument();
    expect(face.textContent).not.toMatch(/SCIPH-FaceOpenCV/);
  });

  it('sin Patients.Identify ni Biometrics.Manage no se muestra el rostro', async () => {
    const { bff } = renderAs(NURSE, backend([ACTIVE]));

    expect(await screen.findByText('Ana Pérez', {}, WAIT)).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Rostro' })).not.toBeInTheDocument();
    expect(bff.calls('GET /api/web/patients/p-1/faces')).toHaveLength(0);
  });
});
