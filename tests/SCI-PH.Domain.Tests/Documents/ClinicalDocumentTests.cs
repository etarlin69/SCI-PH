using SCI_PH.Domain.Entities.Documents;
using SCI_PH.Domain.Enums.Documents;
using SCI_PH.Domain.Exceptions;
using SCI_PH.Domain.ValueObjects;

namespace SCI_PH.Domain.Tests.Documents
{
    public class ClinicalDocumentTests
    {
        private const string Checksum = "9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08";
        private static readonly DateTimeOffset At = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

        private static ClinicalDocument ForEncounter() => ClinicalDocument.RegisterForEncounter(
            Guid.NewGuid(), ClinicalDocumentType.LabResult, "hemograma.pdf", "encounters/abc/hemograma.pdf", "Application/PDF", 2048);

        [Fact]
        public void DocumentHasExactlyOneOwner()
        {
            var patientDocument = ClinicalDocument.RegisterForPatient(
                Guid.NewGuid(), ClinicalDocumentType.IdentityDocument, "cedula.jpg", "patients/p1/cedula.jpg", "image/jpeg", 1024);
            var encounterDocument = ForEncounter();

            Assert.NotNull(patientDocument.PatientId);
            Assert.Null(patientDocument.ClinicalEncounterId);
            Assert.Null(encounterDocument.PatientId);
            Assert.NotNull(encounterDocument.ClinicalEncounterId);
        }

        [Fact]
        public void DocumentStoresMetadataOnly()
        {
            Assert.DoesNotContain(typeof(ClinicalDocument).GetProperties(),
                property => property.PropertyType == typeof(byte[]) || property.PropertyType == typeof(Stream));

            var document = ForEncounter();
            Assert.Equal("application/pdf", document.ContentType);
            Assert.Equal(2048, document.SizeBytes);
            Assert.Equal(ClinicalDocumentStatus.PendingUpload, document.Status);
        }

        [Fact]
        public void EmptyFilesAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ClinicalDocument.RegisterForEncounter(
                Guid.NewGuid(), ClinicalDocumentType.Other, "a.txt", "x/a.txt", "text/plain", 0));
        }

        [Fact]
        public void AvailableDocumentHasChecksumAndUploadDate()
        {
            var document = ForEncounter();

            document.MarkAvailable(Sha256Checksum.Parse(Checksum), At);

            Assert.Equal(ClinicalDocumentStatus.Available, document.Status);
            Assert.Equal(Checksum.ToLowerInvariant(), document.ChecksumSha256!.Value);
            Assert.Equal(At, document.UploadedAtUtc);
            Assert.Throws<DomainException>(() => document.Reject());
        }

        [Fact]
        public void DeletionIsLogical()
        {
            var document = ForEncounter();

            document.MarkDeleted(At);

            Assert.Equal(ClinicalDocumentStatus.Deleted, document.Status);
            Assert.Equal(At, document.DeletedAtUtc);
            Assert.Throws<DomainException>(() => document.MarkDeleted(At));
        }
    }
}
