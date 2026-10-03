using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Enums.Encounters;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Encounters
{
    internal sealed class ClinicalProcedureConfiguration : IEntityTypeConfiguration<ClinicalProcedure>
    {
        public void Configure(EntityTypeBuilder<ClinicalProcedure> builder)
        {
            const string status = nameof(ClinicalProcedure.Status);
            const string performedAt = nameof(ClinicalProcedure.PerformedAtUtc);
            const string performedBy = nameof(ClinicalProcedure.PerformedByStaffId);
            const string completedAt = nameof(ClinicalProcedure.CompletedAtUtc);

            builder.HasKey(x => x.Id);
            // Target of ClinicalResult's composite key: a result can only point to a procedure of its own encounter.
            builder.HasAlternateKey(x => new { x.Id, x.ClinicalEncounterId });
            builder.Property(x => x.Name).HasMaxLength(FieldLengths.Name);
            builder.Property(x => x.Description).HasMaxLength(FieldLengths.LongText);
            builder.Property(x => x.CodeSystem).HasMaxLength(FieldLengths.CodeSystem);
            builder.Property(x => x.Code).HasMaxLength(FieldLengths.Code);
            builder.Property(x => x.Outcome).HasMaxLength(FieldLengths.LongText);

            builder.HasOne(x => x.Encounter).WithMany(x => x.Procedures).HasForeignKey(x => x.ClinicalEncounterId);
            builder.HasOne<ClinicalStaff>().WithMany().HasForeignKey(x => x.PerformedByStaffId);

            builder.HasIndex(x => new { x.ClinicalEncounterId, x.PerformedAtUtc });

            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_ClinicalProcedures_Name", NotBlank(nameof(ClinicalProcedure.Name)));
                table.HasCheckConstraint("CK_ClinicalProcedures_Code",
                    Implies(IsNotNull(nameof(ClinicalProcedure.Code)), IsNotNull(nameof(ClinicalProcedure.CodeSystem))));
                table.HasCheckConstraint("CK_ClinicalProcedures_Lifecycle", All(
                    BothOrNeither(performedAt, performedBy),
                    NotBefore(completedAt, performedAt),
                    Any(
                        All(Is(status, ClinicalProcedureStatus.Planned), IsNull(performedAt), IsNull(completedAt)),
                        All(Is(status, ClinicalProcedureStatus.InProgress), IsNotNull(performedAt), IsNull(completedAt)),
                        All(Is(status, ClinicalProcedureStatus.Completed), IsNotNull(performedAt), IsNotNull(completedAt)),
                        All(Is(status, ClinicalProcedureStatus.Cancelled), IsNull(completedAt)))));
            });
        }
    }
}
