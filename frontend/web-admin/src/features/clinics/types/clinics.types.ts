// Contratos reales de /api/web/admin/clinics (ClinicAdminSummary / RegisterClinicRequest). Solo los campos de
// Clinic en el Domain: no hay email, horario, capacidades ni servicios, y no existe edición ni borrado.

export type ClinicType = 'Hospital' | 'Clinic' | 'PrimaryCareCenter' | 'EmergencyCenter' | 'Other';

export type ClinicStatus = 'Active' | 'Inactive';

export interface ClinicAdmin {
  id: string;
  name: string;
  type: ClinicType;
  facilityCode: string | null;
  province: string;
  municipality: string;
  address: string | null;
  phoneNumber: string | null;
  status: ClinicStatus;
  /** Personal con una afiliación abierta a la clínica. */
  activeStaffCount: number;
}

export interface RegisterClinicRequest {
  name: string;
  type: ClinicType;
  province: string;
  municipality: string;
  facilityCode: string | null;
  address: string | null;
  phoneNumber: string | null;
}

export interface ClinicListQuery {
  search?: string;
  status?: ClinicStatus;
}
