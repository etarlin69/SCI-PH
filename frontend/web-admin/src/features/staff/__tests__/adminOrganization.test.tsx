import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { AuthProvider, Permissions, type WebUser } from '../../../auth';
import { ToastProvider } from '../../../components/shared';
import { AppRoutes } from '../../../routes';
import { ADMIN_USER, createFakeBff, json, problem, session, type RecordedRequest } from '../../../test/fakeBff';
import type { ClinicAdmin } from '../../clinics/types/clinics.types';
import type { ClinicStaffAffiliation, ClinicalStaffAdmin } from '../types/staff.types';

// Administrator real: Permissions.All (incluye Clinics.Manage y ClinicalStaff.Manage).
const ALL_PERMISSIONS = Object.values(Permissions).flatMap((group) => Object.values(group) as string[]);
const ADMIN: WebUser = { ...ADMIN_USER, permissions: ALL_PERMISSIONS };
const WAIT = { timeout: 5000 };

const clinic = (id: string, name: string, status: ClinicAdmin['status'] = 'Active'): ClinicAdmin => ({
  id, name, type: 'Hospital', facilityCode: null, province: 'Distrito Nacional', municipality: 'Santo Domingo',
  address: null, phoneNumber: null, status, activeStaffCount: 0,
});

// Backend con estado: los POST cambian lo que devuelven los GET (como después de F5).
function backend() {
  const clinics: ClinicAdmin[] = [clinic('c1', 'Hospital Central')];
  const staff: ClinicalStaffAdmin[] = [
    {
      id: 's-linked', givenNames: 'Ana', familyNames: 'Vinculada', type: 'Nurse', status: 'Active', professionalLicenseNumber: 'ENF-1',
      specialty: null, phoneNumber: null, accountLinked: true, identityUserId: 'u-1', activeClinics: [{ id: 'c1', name: 'Hospital Central' }],
    },
  ];
  const affiliations: Record<string, ClinicStaffAffiliation[]> = { 's-linked': [] };
  const withClinics = (s: ClinicalStaffAdmin): ClinicalStaffAdmin => ({
    ...s,
    activeClinics: (affiliations[s.id] ?? []).filter((a) => a.isActive).map((a) => ({ id: a.clinicId, name: a.clinicName })),
  });
  const routes: Record<string, (req: RecordedRequest) => Response> = {
    'GET /api/web/admin/clinics': () => json(200, clinics),
    'POST /api/web/admin/clinics': (req) => {
      const body = req.body as Omit<ClinicAdmin, 'id' | 'status' | 'activeStaffCount'>;
      const created: ClinicAdmin = { ...body, id: `c${clinics.length + 1}`, status: 'Active', activeStaffCount: 0 };
      clinics.push(created);
      return json(201, created);
    },
    'GET /api/web/admin/staff': () => json(200, staff.map(withClinics)),
    'POST /api/web/admin/staff': (req) => {
      const body = req.body as Pick<ClinicalStaffAdmin, 'givenNames' | 'familyNames' | 'type' | 'professionalLicenseNumber' | 'specialty' | 'phoneNumber'>;
      const created: ClinicalStaffAdmin = { ...body, id: 's-new', status: 'Active', accountLinked: false, identityUserId: null, activeClinics: [] };
      staff.push(created);
      affiliations['s-new'] = [];
      return json(201, created);
    },
  };
  for (const s of ['s-linked', 's-new']) {
    routes[`GET /api/web/admin/staff/${s}`] = () => json(200, withClinics(staff.find((x) => x.id === s)!));
    routes[`GET /api/web/admin/staff/${s}/affiliations`] = () => json(200, affiliations[s] ?? []);
    routes[`POST /api/web/admin/staff/${s}/affiliations`] = (req) => {
      const { clinicId } = req.body as { clinicId: string };
      const created: ClinicStaffAffiliation = {
        id: `a${(affiliations[s] ?? []).length + 1}`, clinicId, clinicName: clinics.find((c) => c.id === clinicId)!.name,
        clinicStatus: 'Active', startedAtUtc: '2026-10-13T10:00:00Z', endedAtUtc: null, isActive: true,
      };
      (affiliations[s] ??= []).unshift(created);
      return json(201, created);
    };
    for (const a of ['a1', 'a2']) {
      routes[`POST /api/web/admin/staff/${s}/affiliations/${a}/end`] = () => {
        const affiliation = (affiliations[s] ?? []).find((x) => x.id === a)!;
        Object.assign(affiliation, { isActive: false, endedAtUtc: '2026-10-13T12:00:00Z' });
        return json(200, affiliation);
      };
    }
  }
  routes['GET /api/web/admin/clinics/c1'] = () => json(200, clinics[0]);
  routes['POST /api/web/admin/clinics/c1/deactivate'] = () => {
    clinics[0] = { ...clinics[0], status: 'Inactive' };
    return new Response(null, { status: 204 });
  };
  routes['POST /api/web/admin/invitations/staff'] = () =>
    json(201, { id: 'inv-1', subjectType: 2, subjectId: 's-new', email: 'nueva@sci-ph.test', intendedRole: 'Doctor', expiresAtUtc: '2026-10-14T10:00:00Z', emailSent: true });
  return { clinics, staff, routes };
}

function renderAt(path: string, user: WebUser, routes: Record<string, (req: RecordedRequest) => Response>) {
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

describe('administración clínica real: clínicas, personal, afiliaciones e invitación', () => {
  it('clínicas: lista HTTP sin banner de demostración, alta con los campos del Domain y estado tras recargar', { timeout: 15000 }, async () => {
    const b = backend();
    const { bff, view } = renderAt('/admin/clinicas', ADMIN, b.routes);
    const user = userEvent.setup();

    expect(await screen.findByText('Hospital Central', {}, WAIT)).toBeInTheDocument();
    expect(screen.queryByText(/Pantalla de demostración/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Editar|Eliminar/ })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Nueva clínica' }));
    const dialog = await screen.findByRole('dialog', {}, WAIT);
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'Hospital Demo Quinta Vertical');
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo/), 'Hospital');
    await user.type(within(dialog).getByLabelText(/^Provincia/), 'Santiago');
    await user.type(within(dialog).getByLabelText(/^Municipio/), 'Santiago de los Caballeros');
    await user.click(within(dialog).getByRole('button', { name: 'Crear clínica' }));

    await waitFor(() => expect(bff.calls('POST /api/web/admin/clinics')).toHaveLength(1));
    expect(bff.calls('POST /api/web/admin/clinics')[0].body).toEqual({
      name: 'Hospital Demo Quinta Vertical', type: 'Hospital', province: 'Santiago', municipality: 'Santiago de los Caballeros',
      facilityCode: null, address: null, phoneNumber: null,
    });
    expect(await screen.findByText('Hospital Demo Quinta Vertical', {}, WAIT)).toBeInTheDocument();

    // F5: una aplicación nueva lee el mismo backend.
    view.unmount();
    renderAt('/admin/clinicas', ADMIN, b.routes);
    expect(await screen.findByText('Hospital Demo Quinta Vertical', {}, WAIT)).toBeInTheDocument();
  });

  it('clínicas: vacío, error real y desactivación desde la ficha', async () => {
    const { view } = renderAt('/admin/clinicas', ADMIN, { 'GET /api/web/admin/clinics': () => json(200, []) });
    expect(await screen.findByText('No hay clínicas registradas', {}, WAIT)).toBeInTheDocument();
    view.unmount();

    const { view: errorView } = renderAt('/admin/clinicas', ADMIN, { 'GET /api/web/admin/clinics': () => problem(500, 'Server error') });
    expect(await screen.findByRole('button', { name: /Reintentar/ }, WAIT)).toBeInTheDocument();
    errorView.unmount();

    const b = backend();
    const { bff } = renderAt('/admin/clinicas/c1', ADMIN, b.routes);
    const user = userEvent.setup();
    expect(await screen.findByText('Activa', {}, WAIT)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Desactivar' }));
    await user.click(within(await screen.findByRole('alertdialog', {}, WAIT)).getByRole('button', { name: 'Desactivar' }));
    await waitFor(() => expect(bff.calls('POST /api/web/admin/clinics/c1/deactivate')).toHaveLength(1));
    expect(await screen.findByText('Inactiva', {}, WAIT)).toBeInTheDocument();
  });

  it('personal: alta sin cuenta, asignar clínica, invitar con el rol sugerido y finalizar la afiliación', async () => {
    const b = backend();
    const { bff } = renderAt('/admin/personal', ADMIN, b.routes);
    const user = userEvent.setup();

    expect(await screen.findByText('Ana Vinculada', {}, WAIT)).toBeInTheDocument();
    expect(screen.getByText('Cuenta vinculada')).toBeInTheDocument();
    expect(screen.queryByText(/Pantalla de demostración/)).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Nuevo personal' }));
    const dialog = await screen.findByRole('dialog', {}, WAIT);
    expect(within(dialog).queryByLabelText(/email/i)).not.toBeInTheDocument();
    await user.type(within(dialog).getByLabelText(/^Nombres/), 'Laura');
    await user.type(within(dialog).getByLabelText(/^Apellidos/), 'Gómez');
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo de personal/), 'Physician');
    await user.type(within(dialog).getByLabelText(/licencia/i), 'MED-QV-1');
    await user.click(within(dialog).getByRole('button', { name: 'Crear personal' }));

    // Ficha del nuevo personal: sin cuenta y con la acción de invitar.
    expect(await screen.findByRole('heading', { name: 'Laura Gómez' }, WAIT)).toBeInTheDocument();
    expect(bff.calls('POST /api/web/admin/staff')[0].body).toEqual({
      givenNames: 'Laura', familyNames: 'Gómez', type: 'Physician', professionalLicenseNumber: 'MED-QV-1', specialty: null, phoneNumber: null,
    });
    expect(screen.getByText('Sin cuenta')).toBeInTheDocument();

    // Asignar clínica (solo clínicas activas reales).
    await user.click(screen.getByRole('button', { name: 'Asignar clínica' }));
    const assign = await screen.findByRole('dialog', {}, WAIT);
    await waitFor(() => expect(within(assign).getByLabelText(/^Clínica/)).toBeEnabled(), WAIT);
    await user.selectOptions(within(assign).getByLabelText(/^Clínica/), 'c1');
    await user.click(within(assign).getByRole('button', { name: 'Asignar' }));
    await waitFor(() => expect(bff.calls('POST /api/web/admin/staff/s-new/affiliations')).toHaveLength(1));
    expect(bff.calls('POST /api/web/admin/staff/s-new/affiliations')[0].body).toEqual({ clinicId: 'c1' });
    expect(await screen.findByRole('button', { name: 'Finalizar afiliación' }, WAIT)).toBeInTheDocument();

    // Invitar: email + rol sugerido (Physician -> Doctor); el id del personal viaja internamente.
    await user.click(screen.getByRole('button', { name: 'Invitar' }));
    const invite = await screen.findByRole('dialog', {}, WAIT);
    expect(within(invite).getByLabelText(/^Rol de la cuenta/)).toHaveValue('Doctor');
    await user.type(within(invite).getByLabelText(/^Email de la cuenta/), 'nueva@sci-ph.test');
    await user.click(within(invite).getByRole('button', { name: 'Enviar invitación' }));
    await waitFor(() => expect(bff.calls('POST /api/web/admin/invitations/staff')).toHaveLength(1));
    expect(bff.calls('POST /api/web/admin/invitations/staff')[0].body).toEqual({ clinicalStaffId: 's-new', email: 'nueva@sci-ph.test', role: 'Doctor' });
    expect(await screen.findByText(/Invitación enviada a nueva@sci-ph.test/, {}, WAIT)).toBeInTheDocument();

    // Finalizar la afiliación: queda como historial.
    await user.click(screen.getByRole('button', { name: 'Finalizar afiliación' }));
    await user.click(within(await screen.findByRole('alertdialog', {}, WAIT)).getByRole('button', { name: 'Finalizar' }));
    await waitFor(() => expect(bff.calls('POST /api/web/admin/staff/s-new/affiliations/a1/end')).toHaveLength(1));
    expect(await screen.findByText('Histórica', {}, WAIT)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Finalizar afiliación' })).not.toBeInTheDocument();
  });

  it('permisos: sin Users.Manage no se invita; sin Clinics.Manage no hay clínicas; personal vinculado no ofrece invitar', async () => {
    const b = backend();
    const staffOnly: WebUser = { ...ADMIN_USER, permissions: [Permissions.ClinicalStaff.Manage] };
    b.staff.push({ ...b.staff[0], id: 's-new', givenNames: 'Sin', familyNames: 'Cuenta', accountLinked: false, identityUserId: null });
    const { view } = renderAt('/admin/personal/s-new', staffOnly, b.routes);
    expect(await screen.findByRole('heading', { name: 'Sin Cuenta' }, WAIT)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Invitar' })).not.toBeInTheDocument();
    expect(screen.getByText(/requiere el permiso Users.Manage/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Asignar clínica' })).toBeInTheDocument();
    view.unmount();

    const { view: forbidden } = renderAt('/admin/clinicas', staffOnly, b.routes);
    expect(await screen.findByText('ERROR 403', {}, WAIT)).toBeInTheDocument();
    forbidden.unmount();

    renderAt('/admin/personal/s-linked', ADMIN, b.routes);
    expect(await screen.findByRole('heading', { name: 'Ana Vinculada' }, WAIT)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Invitar' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ver cuenta' })).toHaveAttribute('href', '/admin/usuarios/u-1');
  });

  it('muestra los conflictos del backend traducidos', async () => {
    const b = backend();
    renderAt('/admin/clinicas', ADMIN, {
      ...b.routes,
      'POST /api/web/admin/clinics': () => problem(409, 'The request conflicts with the current state.', { detail: 'A clinic with this facility code already exists.' }),
    });
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Nueva clínica' }, WAIT));
    const dialog = await screen.findByRole('dialog', {}, WAIT);
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'Duplicada');
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo/), 'Clinic');
    await user.type(within(dialog).getByLabelText(/^Provincia/), 'P');
    await user.type(within(dialog).getByLabelText(/^Municipio/), 'M');
    await user.type(within(dialog).getByLabelText(/Código de establecimiento/), 'HC-01');
    await user.click(within(dialog).getByRole('button', { name: 'Crear clínica' }));
    expect(await within(dialog).findByText('Ya existe una clínica con ese código de establecimiento.', {}, WAIT)).toBeInTheDocument();
  });
});
