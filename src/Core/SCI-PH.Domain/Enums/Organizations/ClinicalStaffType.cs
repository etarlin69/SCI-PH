namespace SCI_PH.Domain.Enums.Organizations
{
    // Clinical roles only. Application permissions are managed by Identity.
    public enum ClinicalStaffType
    {
        Physician = 1,
        Nurse = 2,
        Paramedic = 3,
        MedicalAssistant = 4,
        OtherClinical = 5
    }
}
