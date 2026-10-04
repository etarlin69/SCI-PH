import type { DateTime } from '../../../types/common';

export type InvitationType = 'Paciente' | 'Personal';
export type InvitationStatus = 'Pendiente' | 'Aceptada' | 'Expirada' | 'Revocada';

export interface Invitation {
  id: string;
  email: string;
  tipo: InvitationType;
  rol?: string;
  departamento?: string;
  estado: InvitationStatus;
  enviadaEl: DateTime;
  enviadaPor: string;
  reenvios: number;
  aceptadaEl?: DateTime;
  mensaje?: string;
}

export interface InvitePatientInput {
  email: string;
  mensaje?: string;
}

export interface InviteStaffInput {
  email: string;
  rol: string;
  departamento: string;
  mensaje?: string;
}

export interface InvitationFilters {
  q: string;
  tipo: InvitationType | '';
  estado: InvitationStatus | '';
}
