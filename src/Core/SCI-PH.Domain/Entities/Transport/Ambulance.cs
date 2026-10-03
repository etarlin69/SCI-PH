using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Enums.Transport;

namespace SCI_PH.Domain.Entities.Transport
{
    public sealed class Ambulance
    {
        private Ambulance() { }

        public Guid Id { get; private set; }
        public string Code { get; private set; } = string.Empty;
        public Guid? BaseClinicId { get; private set; }
        public string? PlateNumber { get; private set; }
        public string? CallSign { get; private set; }
        public AmbulanceStatus Status { get; private set; }

        public static Ambulance Register(string code, Guid? baseClinicId = null, string? plateNumber = null, string? callSign = null) =>
            new()
            {
                Id = Guid.NewGuid(),
                Code = Guard.Text(code, FieldLengths.UnitCode, nameof(code)).ToUpperInvariant(),
                BaseClinicId = Guard.OptionalId(baseClinicId, nameof(baseClinicId)),
                PlateNumber = Guard.OptionalText(plateNumber, FieldLengths.PlateNumber, nameof(plateNumber))?.ToUpperInvariant(),
                CallSign = Guard.OptionalText(callSign, FieldLengths.UnitCode, nameof(callSign)),
                Status = AmbulanceStatus.Available
            };

        public void ChangeStatus(AmbulanceStatus status) => Status = Guard.Defined(status, nameof(status));
    }
}
