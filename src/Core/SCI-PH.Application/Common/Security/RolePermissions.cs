using static SCI_PH.Application.Common.Security.Permissions;

namespace SCI_PH.Application.Common.Security
{
    // Single source of truth for the role -> permission matrix. IdentitySeeder synchronizes it into role claims.
    public static class RolePermissions
    {
        private static readonly string[] _clinicalRead =
            [Patients.Read, Encounters.Read, Medications.Read, Transfers.Read, Documents.Read];

        public static IReadOnlyDictionary<string, IReadOnlyList<string>> ByRole { get; } =
            new Dictionary<string, IReadOnlyList<string>>
            {
                [SecurityRoles.Administrator] = Permissions.All,
                [SecurityRoles.Doctor] =
                [
                    .. _clinicalRead,
                    Patients.Identify,
                    Encounters.Create, Encounters.Update, Encounters.Close,
                    Triage.Write, VitalSigns.Write, ClinicalNotes.Write, Diagnoses.Write,
                    Procedures.Write, Results.Write,
                    Prescriptions.Write, MedicationAdministrations.Write,
                    Transfers.Manage, Documents.Upload
                ],
                [SecurityRoles.Nurse] =
                [
                    .. _clinicalRead,
                    Encounters.Update,
                    Triage.Write, VitalSigns.Write, ClinicalNotes.Write, Procedures.Write,
                    MedicationAdministrations.Write,
                    Documents.Upload
                ],
                [SecurityRoles.Paramedic] =
                [
                    Patients.Read, Patients.Identify,
                    Encounters.Read, Encounters.Create, Encounters.Update,
                    Triage.Write, VitalSigns.Write, ClinicalNotes.Write, Procedures.Write,
                    Medications.Read, MedicationAdministrations.Write,
                    Transfers.Read, Transfers.Manage,
                    Emergencies.Operate,
                    Documents.Upload
                ],
                // Administrative registration only: no access to clinical notes, results or prescriptions.
                [SecurityRoles.Receptionist] =
                [
                    Patients.Read, Patients.Create, Patients.Update, Patients.Identify,
                    Permissions.Biometrics.Manage,
                    Accounts.InvitePatient,
                    Encounters.Create,
                    Transfers.Read, Transfers.Manage,
                    Documents.Upload
                ],
                // Never a generic clinical permission: own-record access is checked per resource.
                [SecurityRoles.Patient] = []
            };

        public static IReadOnlyList<string> For(string role) =>
            ByRole.TryGetValue(role, out var permissions) ? permissions : [];
    }
}
