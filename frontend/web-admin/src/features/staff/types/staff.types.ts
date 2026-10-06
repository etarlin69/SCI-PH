import type { ISODate } from '../../../types/common';

export type StaffStatus = 'Activo' | 'De licencia' | 'Inactivo';

export type Puesto =
  | 'Médico emergenciólogo'
  | 'Médico internista'
  | 'Pediatra'
  | 'Enfermería'
  | 'Paramédico'
  | 'Técnico en emergencias'
  | 'Coordinador de turno'
  | 'Operador de despacho'
  | 'Conductor de ambulancia';

export type Departamento =
  'Emergencias' | 'Cuidados intensivos' | 'Pediatría' | 'Medicina interna' | 'Prehospitalario' | 'Despacho' | 'Administración';

export type StaffAccount =
  { estado: 'vinculada'; userId: string } | { estado: 'pendiente'; invitationId?: string } | { estado: 'sin_acceso' };

export interface StaffMember {
  id: string;
  nombre: string;
  email: string;
  telefono?: string;
  puesto: Puesto;
  clinicId: string;
  departamento: Departamento;
  estado: StaffStatus;
  cuenta: StaffAccount;
  fechaIngreso: ISODate;
}

export interface Assignment {
  id: string;
  staffId: string;
  lugar: string;
  departamento: Departamento;
  turno: string;
  desde: ISODate;
  hasta?: ISODate;
}

export type LicenseStatus = 'Vigente' | 'Por vencer' | 'Vencida';

export interface License {
  id: string;
  staffId: string;
  tipo: string;
  numero: string;
  vence?: ISODate;
}

export type HistoryTone = 'info' | 'success' | 'warning' | 'danger';

export interface StaffHistoryEntry {
  id: string;
  staffId: string;
  fecha: ISODate;
  titulo: string;
  detalle: string;
  tone: HistoryTone;
}

export interface StaffInput {
  nombre: string;
  email: string;
  telefono?: string;
  puesto: Puesto;
  clinicId: string;
  departamento: Departamento;
  estado: StaffStatus;
}

export interface StaffAccessInput {
  invitar: boolean;
  rol?: string;
}

export type AssignmentInput = Omit<Assignment, 'id' | 'staffId' | 'hasta'>;
export type LicenseInput = Omit<License, 'id' | 'staffId'>;

export interface StaffFilters {
  q: string;
  puesto: Puesto | '';
  clinicId: string;
  estado: StaffStatus | '';
}
