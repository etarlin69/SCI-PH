// DTOs de /api/web/admin/invitations (SCI-PH.Application/DTOs/Security/InvitationModels.cs).
// El token de la invitación solo viaja en el email: nunca aparece en una respuesta.

/** InvitationSubjectType (enum numérico en JSON). */
export const InvitationSubjectType = { Patient: 1, ClinicalStaff: 2 } as const;
export type InvitationSubjectTypeCode = (typeof InvitationSubjectType)[keyof typeof InvitationSubjectType];

export interface InvitationSummary {
  id: string;
  subjectType: InvitationSubjectTypeCode;
  subjectId: string;
  email: string;
  intendedRole: string;
  expiresAtUtc: string;
  emailSent: boolean;
}

/** Invita a un Patient existente; el email sale de su identificador de email activo. */
export interface InvitePatientRequest {
  patientId: string;
}

/** Invita a un ClinicalStaff existente con un rol de personal (nunca Administrator). */
export interface InviteStaffRequest {
  clinicalStaffId: string;
  email: string;
  role: string;
}
