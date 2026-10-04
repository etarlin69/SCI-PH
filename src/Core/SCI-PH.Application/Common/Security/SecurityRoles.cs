namespace SCI_PH.Application.Common.Security
{
    // Account roles. A role describes what a person may do in the system; it is not clinical data.
    public static class SecurityRoles
    {
        public const string Administrator = "Administrator";
        public const string Doctor = "Doctor";
        public const string Nurse = "Nurse";
        public const string Paramedic = "Paramedic";
        public const string Receptionist = "Receptionist";
        // Patient accounts get no generic permission: access to their own record is resource-based.
        public const string Patient = "Patient";

        public static IReadOnlyList<string> All { get; } =
            [Administrator, Doctor, Nurse, Paramedic, Receptionist, Patient];

        // Roles a staff invitation may grant. Administrator is only assigned by an administrator.
        public static IReadOnlyList<string> InvitableStaff { get; } = [Doctor, Nurse, Paramedic, Receptionist];
    }
}
