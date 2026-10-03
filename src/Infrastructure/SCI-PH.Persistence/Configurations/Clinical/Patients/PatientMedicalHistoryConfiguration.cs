using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Entities.Patients;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Patients
{
    internal sealed class PatientMedicalHistoryConfiguration : IEntityTypeConfiguration<PatientMedicalHistory>
    {
        public void Configure(EntityTypeBuilder<PatientMedicalHistory> builder)
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Notes).HasMaxLength(FieldLengths.LongText);

            builder.HasOne(x => x.Patient).WithMany(x => x.MedicalHistories).HasForeignKey(x => x.PatientId);
            builder.HasOne<ClinicalStaff>().WithMany().HasForeignKey(x => x.RecordedByStaffId);

            builder.HasIndex(x => new { x.PatientId, x.Version }).IsUnique();

            builder.ToTable(table => table.HasCheckConstraint(
                "CK_PatientMedicalHistories_Version", $"{Column(nameof(PatientMedicalHistory.Version))} >= 1"));
        }
    }
}
