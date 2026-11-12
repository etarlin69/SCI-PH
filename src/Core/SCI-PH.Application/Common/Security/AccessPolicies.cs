namespace SCI_PH.Application.Common.Security
{
    // Named authorization policies that are not a single permission (each permission is its own policy, Permissions).
    public static class AccessPolicies
    {
        // Coarse entry gate of the patient self-service (ADR-015): an authenticated account with the Patient role, which
        // has no permissions by design. It is not the resource boundary: PatientSelfService still resolves the record
        // from Patient.IdentityUserId and refuses an account without that link.
        public const string PatientSelfService = "PatientSelfService";
    }
}
