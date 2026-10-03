using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.ValueObjects;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Encounters
{
    internal sealed class ClinicalEncounterConfiguration : IEntityTypeConfiguration<ClinicalEncounter>
    {
        public void Configure(EntityTypeBuilder<ClinicalEncounter> builder)
        {
            const string patientId = nameof(ClinicalEncounter.PatientId);
            const string temporaryCode = nameof(ClinicalEncounter.TemporaryPatientCode);
            const string identifiedAt = nameof(ClinicalEncounter.IdentifiedAtUtc);
            const string identifiedBy = nameof(ClinicalEncounter.IdentifiedByStaffId);
            const string type = nameof(ClinicalEncounter.Type);
            const string startedAt = nameof(ClinicalEncounter.StartedAtUtc);
            const string endedAt = nameof(ClinicalEncounter.EndedAtUtc);

            builder.HasKey(x => x.Id);
            builder.Property(x => x.Reason).HasMaxLength(FieldLengths.LongText);
            builder.Property(x => x.TemporaryPatientCode)
                .HasConversion(code => code!.Value, value => TemporaryPatientCode.Parse(value))
                .HasMaxLength(TemporaryPatientCode.MaxLength);

            // Detects concurrent identification or status changes from different channels (web, mobile, device).
            builder.Property(x => x.PatientId).IsConcurrencyToken();
            builder.Property(x => x.Status).IsConcurrencyToken();

            builder.HasOne(x => x.Patient).WithMany(x => x.Encounters).HasForeignKey(x => x.PatientId);
            builder.HasOne(x => x.Clinic).WithMany(x => x.Encounters).HasForeignKey(x => x.ClinicId);
            builder.HasOne(x => x.Ambulance).WithMany().HasForeignKey(x => x.AmbulanceId);
            builder.HasOne<ClinicalStaff>().WithMany().HasForeignKey(x => x.IdentifiedByStaffId);

            builder.HasIndex(x => x.TemporaryPatientCode).IsUnique();
            builder.HasIndex(x => new { x.PatientId, x.StartedAtUtc });
            builder.HasIndex(x => new { x.ClinicId, x.Status });
            builder.HasIndex(x => new { x.Status, x.StartedAtUtc });

            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_ClinicalEncounters_Identification", Any(
                    // NN still unidentified: only emergency or prehospital care can start without a patient.
                    All(IsNull(patientId), IsNotNull(temporaryCode), IsNull(identifiedAt), IsNull(identifiedBy),
                        In(type, ClinicalEncounter.TypesAllowingUnidentifiedPatient)),
                    // Patient known from the start: no NN code.
                    All(IsNotNull(patientId), IsNull(temporaryCode), IsNull(identifiedAt), IsNull(identifiedBy)),
                    // NN identified later: the code is kept as history.
                    All(IsNotNull(patientId), IsNotNull(temporaryCode), IsNotNull(identifiedAt), IsNotNull(identifiedBy),
                        NotBefore(identifiedAt, startedAt))));
                table.HasCheckConstraint("CK_ClinicalEncounters_Clinic",
                    Any(Is(type, ClinicalEncounterType.Prehospital), IsNotNull(nameof(ClinicalEncounter.ClinicId))));
                table.HasCheckConstraint("CK_ClinicalEncounters_End", All(
                    $"({In(nameof(ClinicalEncounter.Status), ClinicalEncounter.FinalStatuses)}) = ({IsNotNull(endedAt)})",
                    NotBefore(endedAt, startedAt)));
            });
        }
    }
}
