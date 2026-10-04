using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Enums.Encounters;

namespace SCI_PH.Domain.Entities.Encounters
{
    // Notes are immutable; a correction or follow-up is written as a new note.
    public sealed class ClinicalNote
    {
        private ClinicalNote() { }

        public Guid Id { get; private set; }
        public Guid ClinicalEncounterId { get; private set; }
        public ClinicalEncounter Encounter { get; private set; } = null!;
        public Guid AuthorStaffId { get; private set; }
        public ClinicalNoteType Type { get; private set; }
        public string Content { get; private set; } = string.Empty;
        // Clinical time the note refers to; CreatedAtUtc is when it was written.
        public DateTimeOffset OccurredAtUtc { get; private set; }
        public DateTimeOffset CreatedAtUtc { get; private set; }

        public static ClinicalNote Write(
            Guid clinicalEncounterId,
            Guid authorStaffId,
            ClinicalNoteType type,
            string content,
            DateTimeOffset occurredAtUtc) =>
            new()
            {
                Id = Guid.NewGuid(),
                ClinicalEncounterId = Guard.Id(clinicalEncounterId, nameof(clinicalEncounterId)),
                AuthorStaffId = Guard.Id(authorStaffId, nameof(authorStaffId)),
                Type = Guard.Defined(type, nameof(type)),
                Content = Guard.Text(content, FieldLengths.NoteContent, nameof(content)),
                OccurredAtUtc = Guard.Utc(occurredAtUtc),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
    }
}
