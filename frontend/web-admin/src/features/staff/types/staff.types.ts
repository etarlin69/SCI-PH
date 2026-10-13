// Contratos reales de /api/web/admin/staff (ClinicalStaffAdminSummary, afiliaciones). Solo los campos de ClinicalStaff
// en el Domain: el email no forma parte del personal clínico (pertenece a la invitación / cuenta de Identity).

export type ClinicalStaffType = 'Physician' | 'Nurse' | 'Paramedic' | 'MedicalAssistant' | 'OtherClinical';

export type StaffStatus = 'Active' | 'Inactive';

export interface ClinicRef {
  id: string;
  name: string;
}

export interface ClinicalStaffAdmin {
  id: string;
  givenNames: string;
  familyNames: string;
  type: ClinicalStaffType;
  status: StaffStatus;
  professionalLicenseNumber: string | null;
  specialty: string | null;
  phoneNumber: string | null;
  /** ClinicalStaff.IdentityUserId presente: la cuenta (Identity) está vinculada. */
  accountLinked: boolean;
  identityUserId: string | null;
  /** Clínicas con afiliación abierta. */
  activeClinics: ClinicRef[];
}

export interface RegisterClinicalStaffRequest {
  givenNames: string;
  familyNames: string;
  type: ClinicalStaffType;
  professionalLicenseNumber: string | null;
  specialty: string | null;
  phoneNumber: string | null;
}

export interface ClinicStaffAffiliation {
  id: string;
  clinicId: string;
  clinicName: string;
  clinicStatus: 'Active' | 'Inactive';
  startedAtUtc: string;
  endedAtUtc: string | null;
  isActive: boolean;
}

export interface StaffListQuery {
  search?: string;
  status?: StaffStatus;
}
