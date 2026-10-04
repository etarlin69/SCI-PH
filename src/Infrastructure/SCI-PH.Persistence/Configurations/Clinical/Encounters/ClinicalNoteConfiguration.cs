using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Organizations;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Encounters
{
    internal sealed class ClinicalNoteConfiguration : IEntityTypeConfiguration<ClinicalNote>
    {
        public void Configure(EntityTypeBuilder<ClinicalNote> builder)
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Content).HasMaxLength(FieldLengths.NoteContent);

            builder.HasOne(x => x.Encounter).WithMany(x => x.Notes).HasForeignKey(x => x.ClinicalEncounterId);
            builder.HasOne<ClinicalStaff>().WithMany().HasForeignKey(x => x.AuthorStaffId);

            builder.HasIndex(x => new { x.ClinicalEncounterId, x.OccurredAtUtc });

            builder.ToTable(table => table.HasCheckConstraint("CK_ClinicalNotes_Content", NotBlank(nameof(ClinicalNote.Content))));
        }
    }
}
