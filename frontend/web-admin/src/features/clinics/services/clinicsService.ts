import { CLINICS_MOCK, CLINIC_SERVICES_MOCK, PROVINCIAS, STAFF_MOCK } from '../../../data/mock';
import type { Clinic, ClinicInput, ClinicService, ClinicStatus, StaffMember } from '../../../types';
import { ServiceError } from '../../../services/core/errors';
import { mockList, mockRead, mockWrite, nextId, notFound } from '../../../services/core/mock';
import { record } from '../../audit/services/auditService';

const clinics: Clinic[] = [...CLINICS_MOCK];
const services: ClinicService[] = [...CLINIC_SERVICES_MOCK];

function withCounts(c: Clinic): Clinic {
  return {
    ...c,
    personal: STAFF_MOCK.filter((s) => s.clinicId === c.id).length,
    // DEMO: los dispositivos reales (Device API) no se asocian a clínicas en el backend.
    dispositivos: 0,
  };
}

function find(id: string): Clinic {
  return clinics.find((c) => c.id === id) ?? notFound('La clínica');
}

function assertUnique(nombre: string, exceptId?: string): void {
  if (clinics.some((c) => c.id !== exceptId && c.nombre.toLowerCase() === nombre.trim().toLowerCase())) {
    throw new ServiceError('CONFLICT', 'Ya existe una clínica con ese nombre.', { field: 'nombre' });
  }
}

export const clinicsService = {
  provincias: PROVINCIAS,

  list(): Promise<Clinic[]> {
    return mockList(() => clinics.map(withCounts));
  },

  /** Opciones para selects (sin latencia de lista vacía). */
  options(): Promise<Pick<Clinic, 'id' | 'nombre' | 'estado'>[]> {
    return mockRead(() => clinics.map(({ id, nombre, estado }) => ({ id, nombre, estado })));
  },

  get(id: string): Promise<Clinic> {
    return mockRead(() => withCounts(find(id)));
  },

  create(input: ClinicInput): Promise<Clinic> {
    return mockWrite('clinics.create', () => {
      assertUnique(input.nombre);
      const clinic: Clinic = {
        ...input,
        id: nextId(
          'CLI-',
          clinics.map((c) => c.id),
        ),
        personal: 0,
        dispositivos: 0,
        horario: '24 h · 7 días',
      };
      clinics.push(clinic);
      record({ accion: 'Crear', recurso: `Clínica ${clinic.id}`, descripcion: `Alta de ${clinic.nombre}.` });
      return clinic;
    });
  },

  update(id: string, input: ClinicInput): Promise<Clinic> {
    return mockWrite('clinics.update', () => {
      const clinic = find(id);
      assertUnique(input.nombre, id);
      Object.assign(clinic, input);
      record({ accion: 'Actualizar', recurso: `Clínica ${id}`, descripcion: 'Datos de la clínica actualizados.' });
      return withCounts(clinic);
    });
  },

  setStatus(id: string, estado: ClinicStatus): Promise<Clinic> {
    return mockWrite('clinics.status', () => {
      const clinic = find(id);
      const antes = clinic.estado;
      clinic.estado = estado;
      record({
        accion: 'Actualizar',
        recurso: `Clínica ${id}`,
        descripcion: `Clínica ${estado === 'Activa' ? 'reactivada' : 'desactivada'}.`,
        cambios: [{ campo: 'Estado', antes, despues: estado }],
      });
      return withCounts(clinic);
    });
  },

  /** Error IN_USE (details.personal, details.dispositivos) si tiene recursos asignados. */
  remove(id: string): Promise<void> {
    return mockWrite('clinics.remove', () => {
      const clinic = withCounts(find(id));
      if (clinic.personal || clinic.dispositivos) {
        throw new ServiceError('IN_USE', 'Tiene personal o dispositivos asignados.', {
          personal: clinic.personal,
          dispositivos: clinic.dispositivos,
        });
      }
      clinics.splice(
        clinics.findIndex((c) => c.id === id),
        1,
      );
      record({ accion: 'Eliminar', recurso: `Clínica ${id}`, descripcion: `${clinic.nombre} eliminada del directorio.` });
    });
  },

  listServices(clinicId: string): Promise<ClinicService[]> {
    return mockList(() => services.filter((s) => s.clinicId === clinicId));
  },

  setServiceAvailability(clinicId: string, serviceId: string, disponible: boolean): Promise<ClinicService> {
    return mockWrite('clinics.service', () => {
      const s = services.find((x) => x.id === serviceId && x.clinicId === clinicId) ?? notFound('El servicio');
      s.disponible = disponible;
      record({
        accion: 'Actualizar',
        recurso: `Clínica ${clinicId}`,
        descripcion: `${s.nombre}: ${disponible ? 'disponible' : 'no disponible'} para traslados.`,
      });
      return s;
    });
  },

  listStaff(clinicId: string): Promise<StaffMember[]> {
    return mockList(() => STAFF_MOCK.filter((s) => s.clinicId === clinicId));
  },

};
