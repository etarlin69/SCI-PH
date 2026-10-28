using System.Reflection;

namespace SCI_PH.Application.Common.Security
{
    // Permission names are also the authorization policy names. They are granted through role claims.
    public static class Permissions
    {
        public static class Users
        {
            public const string Manage = "Users.Manage";
        }

        public static class Accounts
        {
            // Patient account invitations only. Staff invitations require Users.Manage.
            public const string InvitePatient = "Accounts.InvitePatient";
        }

        public static class Devices
        {
            public const string Manage = "Devices.Manage";
        }

        public static class Audit
        {
            public const string Read = "Audit.Read";
        }

        // Clinical organization master data (not Identity): clinics of the network.
        public static class Clinics
        {
            public const string Manage = "Clinics.Manage";
        }

        // Clinical staff records and their clinic affiliations. Inviting the staff member to an account is Users.Manage.
        public static class ClinicalStaff
        {
            public const string Manage = "ClinicalStaff.Manage";
        }

        public static class Patients
        {
            public const string Read = "Patients.Read";
            public const string Create = "Patients.Create";
            public const string Update = "Patients.Update";
            public const string Identify = "Patients.Identify";
        }

        public static class Encounters
        {
            public const string Read = "Encounters.Read";
            public const string Create = "Encounters.Create";
            public const string Update = "Encounters.Update";
            public const string Close = "Encounters.Close";
        }

        public static class Triage
        {
            public const string Write = "Triage.Write";
        }

        public static class VitalSigns
        {
            public const string Write = "VitalSigns.Write";
        }

        public static class ClinicalNotes
        {
            public const string Write = "ClinicalNotes.Write";
        }

        public static class Diagnoses
        {
            public const string Write = "Diagnoses.Write";
        }

        public static class Procedures
        {
            public const string Write = "Procedures.Write";
        }

        public static class Results
        {
            public const string Write = "Results.Write";
        }

        public static class Medications
        {
            public const string Read = "Medications.Read";
        }

        public static class Prescriptions
        {
            public const string Write = "Prescriptions.Write";
        }

        public static class MedicationAdministrations
        {
            public const string Write = "MedicationAdministrations.Write";
        }

        public static class Transfers
        {
            public const string Read = "Transfers.Read";
            public const string Manage = "Transfers.Manage";
        }

        // Prehospital care in the field (paramedic flow of the mobile app). The /api/mobile/emergencies endpoints will
        // require it in addition to the clinical permissions of each action.
        public static class Emergencies
        {
            public const string Operate = "Emergencies.Operate";
        }

        // Fingerprint references of a patient: enrollment (and, later, revocation) on a reader. Never a clinical record and
        // never enough by itself: the actor must also be active clinical staff who can see the patient.
        public static class Biometrics
        {
            public const string Manage = "Biometrics.Manage";
        }

        public static class Documents
        {
            public const string Read = "Documents.Read";
            public const string Upload = "Documents.Upload";
        }

        public static IReadOnlyList<string> All { get; } = typeof(Permissions)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.Static)
            .SelectMany(group => group.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();
    }
}
