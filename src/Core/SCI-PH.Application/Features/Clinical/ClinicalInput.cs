using SCI_PH.Application.Common.Errors;

namespace SCI_PH.Application.Features.Clinical
{
    // Request rules checked before the Domain runs, and translation of the Domain's argument guards.
    internal static class ClinicalInput
    {
        // Domain factories reject invalid input with ArgumentException (Guard). Only these calls are translated to
        // a 400: an ArgumentException anywhere else is still a programming error and stays a 500.
        public static T Apply<T>(Func<T> domainCall)
        {
            try
            {
                return domainCall();
            }
            catch (ArgumentException exception)
            {
                throw new RequestValidationException(exception.Message, ToField(exception.ParamName), exception);
            }
        }

        public static void Apply(Action domainCall) => Apply(() =>
        {
            domainCall();
            return true;
        });

        public static TEnum Defined<TEnum>(TEnum value, string field) where TEnum : struct, Enum =>
            Enum.IsDefined(value) ? value : throw new RequestValidationException($"{field} has an unknown value.", field);

        public static Guid Id(Guid value, string field) =>
            value == Guid.Empty ? throw new RequestValidationException($"{field} is required.", field) : value;

        // Clinical timestamps cannot be in the future and cannot precede the encounter.
        // Guard.Utc only normalizes the offset, so these rules live here, against the injected clock.
        public static DateTimeOffset ClinicalTime(DateTimeOffset value, DateTimeOffset now, DateTimeOffset encounterStartedAtUtc, string field)
        {
            if (value > now)
            {
                throw new RequestValidationException($"{field} cannot be in the future.", field);
            }

            return value < encounterStartedAtUtc
                ? throw new RequestValidationException($"{field} cannot be earlier than the start of the encounter.", field)
                : value;
        }

        private static string? ToField(string? paramName) =>
            string.IsNullOrEmpty(paramName) ? null : char.ToLowerInvariant(paramName[0]) + paramName[1..];
    }
}
