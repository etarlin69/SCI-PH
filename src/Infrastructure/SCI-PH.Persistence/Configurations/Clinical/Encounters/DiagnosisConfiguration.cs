using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Enums.Encounters;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Encounters
{
    internal sealed class DiagnosisConfiguration : IEntityTypeConfiguration<Diagnosis>
    {
        public void Configure(EntityTypeBuilder<Diagnosis> builder)
        {
            const string confirmedAt = nameof(Diagnosis.ConfirmedAtUtc);

            builder.HasKey(x => x.Id);
            builder.Property(x => x.Description).HasMaxLength(FieldLengths.ShortText);
            builder.Property(x => x.CodeSystem).HasMaxLength(FieldLengths.CodeSystem);
            builder.Property(x => x.Code).HasMaxLength(FieldLengths.Code);

            builder.HasOne(x => x.Encounter).WithMany(x => x.Diagnoses).HasForeignKey(x => x.ClinicalEncounterId);
            builder.HasOne<ClinicalStaff>().WithMany().HasForeignKey(x => x.RecordedByStaffId);

            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_Diagnoses_Description", NotBlank(nameof(Diagnosis.Description)));
                table.HasCheckConstraint("CK_Diagnoses_Code",
                    Implies(IsNotNull(nameof(Diagnosis.Code)), IsNotNull(nameof(Diagnosis.CodeSystem))));
                table.HasCheckConstraint("CK_Diagnoses_Confirmation", All(
                    $"({Is(nameof(Diagnosis.Type), DiagnosisType.Definitive)}) = ({IsNotNull(confirmedAt)})",
                    NotBefore(confirmedAt, nameof(Diagnosis.RecordedAtUtc))));
            });
        }
    }
}
