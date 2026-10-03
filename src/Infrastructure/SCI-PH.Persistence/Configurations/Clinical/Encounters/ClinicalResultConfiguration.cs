using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Enums.Encounters;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Encounters
{
    internal sealed class ClinicalResultConfiguration : IEntityTypeConfiguration<ClinicalResult>
    {
        private const int NumericPrecision = 18;
        private const int NumericScale = 6;

        public void Configure(EntityTypeBuilder<ClinicalResult> builder)
        {
            const string status = nameof(ClinicalResult.Status);
            const string resultedAt = nameof(ClinicalResult.ResultedAtUtc);

            builder.HasKey(x => x.Id);
            builder.Property(x => x.Name).HasMaxLength(FieldLengths.Name);
            builder.Property(x => x.CodeSystem).HasMaxLength(FieldLengths.CodeSystem);
            builder.Property(x => x.Code).HasMaxLength(FieldLengths.Code);
            builder.Property(x => x.ResultText).HasMaxLength(FieldLengths.LongText);
            builder.Property(x => x.NumericValue).HasPrecision(NumericPrecision, NumericScale);
            builder.Property(x => x.Unit).HasMaxLength(FieldLengths.MeasurementUnit);
            builder.Property(x => x.ReferenceRange).HasMaxLength(FieldLengths.ReferenceRange);

            builder.HasOne(x => x.Encounter).WithMany(x => x.Results).HasForeignKey(x => x.ClinicalEncounterId);
            builder.HasOne<ClinicalProcedure>().WithMany()
                .HasForeignKey(x => new { x.ClinicalProcedureId, x.ClinicalEncounterId })
                .HasPrincipalKey(x => new { x.Id, x.ClinicalEncounterId });
            builder.HasOne<ClinicalStaff>().WithMany().HasForeignKey(x => x.RecordedByStaffId);

            builder.HasIndex(x => new { x.ClinicalEncounterId, x.ResultedAtUtc });

            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_ClinicalResults_Name", NotBlank(nameof(ClinicalResult.Name)));
                table.HasCheckConstraint("CK_ClinicalResults_Code",
                    Implies(IsNotNull(nameof(ClinicalResult.Code)), IsNotNull(nameof(ClinicalResult.CodeSystem))));
                table.HasCheckConstraint("CK_ClinicalResults_Value", All(
                    Implies(In(status, [ClinicalResultStatus.Pending, ClinicalResultStatus.Cancelled]), IsNull(resultedAt)),
                    Implies(In(status, [ClinicalResultStatus.Final, ClinicalResultStatus.Corrected]), All(
                        IsNotNull(resultedAt),
                        Any(IsNotNull(nameof(ClinicalResult.ResultText)), IsNotNull(nameof(ClinicalResult.NumericValue)))))));
            });
        }
    }
}
