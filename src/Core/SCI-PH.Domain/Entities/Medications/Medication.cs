using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Enums.Medications;

namespace SCI_PH.Domain.Entities.Medications
{
    // Catalog entry only. Prescriptions, home medication and administrations reference it.
    public sealed class Medication
    {
        private Medication() { }

        public Guid Id { get; private set; }
        public string? Code { get; private set; }
        public string Name { get; private set; } = string.Empty;
        // Pharmaceutical form, for example "tablet" or "injectable solution".
        public string? Presentation { get; private set; }
        // Strength, for example "500 mg" or "5 mg/mL".
        public string? Concentration { get; private set; }
        public MedicationRoute? DefaultRoute { get; private set; }
        public MedicationStatus Status { get; private set; }

        public static Medication Register(
            string name,
            string? presentation = null,
            string? concentration = null,
            string? code = null,
            MedicationRoute? defaultRoute = null) =>
            new()
            {
                Id = Guid.NewGuid(),
                Name = Guard.Text(name, FieldLengths.Name, nameof(name)),
                Presentation = Guard.OptionalText(presentation, FieldLengths.Code, nameof(presentation)),
                Concentration = Guard.OptionalText(concentration, FieldLengths.Code, nameof(concentration)),
                Code = Guard.OptionalText(code, FieldLengths.Code, nameof(code))?.ToUpperInvariant(),
                DefaultRoute = Guard.OptionalDefined(defaultRoute, nameof(defaultRoute)),
                Status = MedicationStatus.Active
            };

        public void Deactivate() => Status = MedicationStatus.Inactive;

        public void Activate() => Status = MedicationStatus.Active;
    }
}
