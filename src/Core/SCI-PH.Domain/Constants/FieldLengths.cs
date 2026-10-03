namespace SCI_PH.Domain.Constants
{
    // Shared by domain validation and EF Core column sizes so both layers stay aligned.
    public static class FieldLengths
    {
        public const int PersonName = 120;
        public const int Name = 200;
        public const int CodeSystem = 40;
        public const int Code = 80;
        public const int ShortText = 500;
        public const int LongText = 4000;
        public const int NoteContent = 8000;
        public const int IdentifierValue = 250;
        public const int Issuer = 120;
        public const int PhoneNumber = 32;
        public const int Address = 250;
        public const int Region = 100;
        public const int ProviderName = 120;
        public const int ExternalReference = 200;
        public const int LicenseNumber = 80;
        public const int Specialty = 120;
        public const int PlateNumber = 30;
        public const int UnitCode = 60;
        public const int MeasurementUnit = 24;
        public const int Frequency = 120;
        public const int ReferenceRange = 120;
        public const int FileName = 255;
        public const int BlobPath = 1024;
        public const int ContentType = 150;
        public const int AuditEntityName = 160;
        public const int AuditEntityId = 80;
    }
}
