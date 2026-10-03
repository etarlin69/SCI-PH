using SCI_PH.Domain.Common;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Domain.Entities.Organizations
{
    // Employment period of a staff member at a clinic. A person can leave and later return,
    // so only one active (not ended) affiliation per clinic and staff member is allowed.
    public sealed class ClinicStaffAffiliation
    {
        private ClinicStaffAffiliation() { }

        public Guid Id { get; private set; }
        public Guid ClinicId { get; private set; }
        public Guid ClinicalStaffId { get; private set; }
        public DateTimeOffset StartedAtUtc { get; private set; }
        public DateTimeOffset? EndedAtUtc { get; private set; }

        public bool IsActive => EndedAtUtc is null;

        public static ClinicStaffAffiliation Start(Guid clinicId, Guid clinicalStaffId, DateTimeOffset startedAtUtc) =>
            new()
            {
                Id = Guid.NewGuid(),
                ClinicId = Guard.Id(clinicId, nameof(clinicId)),
                ClinicalStaffId = Guard.Id(clinicalStaffId, nameof(clinicalStaffId)),
                StartedAtUtc = Guard.Utc(startedAtUtc)
            };

        public void End(DateTimeOffset endedAtUtc)
        {
            if (!IsActive)
            {
                throw new DomainException("The affiliation has already ended.");
            }

            EndedAtUtc = Guard.NotBefore(endedAtUtc, StartedAtUtc, nameof(endedAtUtc));
        }
    }
}
