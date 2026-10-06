export type ClinicType = 'Hospital' | 'Clínica' | 'Centro de diagnóstico';
export type ClinicStatus = 'Activa' | 'Inactiva';

export interface Clinic {
  id: string;
  nombre: string;
  tipo: ClinicType;
  direccion: string;
  provincia: string;
  telefono: string;
  email: string;
  contacto: string;
  estado: ClinicStatus;
  personal: number;
  dispositivos: number;
  horario: string;
  lat?: number;
  lng?: number;
}

export interface ClinicServiceCapacity {
  ocupadas: number;
  total: number;
  unidad: string;
}

export interface ClinicService {
  id: string;
  clinicId: string;
  nombre: string;
  disponible: boolean;
  capacidad?: ClinicServiceCapacity;
  nota?: string;
  alerta?: boolean;
}

export type ClinicInput = Pick<Clinic, 'nombre' | 'tipo' | 'direccion' | 'provincia' | 'telefono' | 'email' | 'contacto' | 'estado'>;

export interface ClinicFilters {
  q: string;
  provincia: string;
  estado: ClinicStatus | '';
}
