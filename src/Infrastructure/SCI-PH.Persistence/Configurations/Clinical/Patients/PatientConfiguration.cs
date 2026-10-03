using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Patients;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Patients
{
    internal sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
    {
        public void Configure(EntityTypeBuilder<Patient> builder)
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.GivenNames).HasMaxLength(FieldLengths.PersonName);
            builder.Property(x => x.FamilyNames).HasMaxLength(FieldLengths.PersonName);

            // One clinical record per account; unlinked patients (NULL) are not constrained.
            builder.HasIndex(x => x.IdentityUserId).IsUnique();
            builder.HasIndex(x => new { x.FamilyNames, x.GivenNames });

            builder.ToTable(table => table.HasCheckConstraint(
                "CK_Patients_Names",
                All(NotBlank(nameof(Patient.GivenNames)), NotBlank(nameof(Patient.FamilyNames)))));
        }
    }
}
