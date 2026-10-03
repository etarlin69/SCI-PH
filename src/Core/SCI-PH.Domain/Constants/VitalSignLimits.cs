using SCI_PH.Domain.Enums.Encounters;

namespace SCI_PH.Domain.Constants
{
    // Plausibility limits that reject typing errors; they are not clinical alert thresholds.
    // Units follow UCUM notation. Persistence builds a CHECK constraint from this same table.
    public static class VitalSignLimits
    {
        public static IReadOnlyDictionary<VitalSignType, VitalSignLimit> ByType { get; } = new Dictionary<VitalSignType, VitalSignLimit>
        {
            [VitalSignType.HeartRate] = new("/min", 0m, 300m),
            [VitalSignType.RespiratoryRate] = new("/min", 0m, 100m),
            [VitalSignType.SystolicBloodPressure] = new("mm[Hg]", 0m, 300m),
            [VitalSignType.DiastolicBloodPressure] = new("mm[Hg]", 0m, 250m),
            [VitalSignType.Temperature] = new("Cel", 25m, 45m),
            [VitalSignType.OxygenSaturation] = new("%", 0m, 100m),
            [VitalSignType.Weight] = new("kg", 0.2m, 500m),
            [VitalSignType.Height] = new("cm", 20m, 280m),
            [VitalSignType.BloodGlucose] = new("mg/dL", 0m, 2000m),
            [VitalSignType.PainScore] = new("{score}", 0m, 10m),
            [VitalSignType.GlasgowComaScale] = new("{score}", 3m, 15m)
        };

        public static VitalSignLimit For(VitalSignType type) =>
            ByType.TryGetValue(type, out var limit)
                ? limit
                : throw new ArgumentOutOfRangeException(nameof(type), type, "No limits are defined for this vital sign.");
    }

    public sealed record VitalSignLimit(string Unit, decimal Minimum, decimal Maximum);
}
