namespace SCI_PH.Domain.Common
{
    internal static class Guard
    {
        public static Guid Id(Guid value, string paramName) =>
            value == Guid.Empty ? throw new ArgumentException("An identifier is required.", paramName) : value;

        public static Guid? OptionalId(Guid? value, string paramName) =>
            value == Guid.Empty ? throw new ArgumentException("An identifier cannot be empty.", paramName) : value;

        public static string Text(string? value, int maxLength, string paramName)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                throw new ArgumentException("A value is required.", paramName);
            }

            return MaxLength(trimmed, maxLength, paramName);
        }

        public static string? OptionalText(string? value, int maxLength, string paramName)
        {
            var trimmed = value?.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : MaxLength(trimmed, maxLength, paramName);
        }

        public static TEnum Defined<TEnum>(TEnum value, string paramName) where TEnum : struct, Enum =>
            Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(paramName, value, "Unknown value.");

        public static TEnum? OptionalDefined<TEnum>(TEnum? value, string paramName) where TEnum : struct, Enum =>
            value is null ? null : Defined(value.Value, paramName);

        // Clinical timestamps are stored in UTC (PostgreSQL timestamptz with a zero offset).
        public static DateTimeOffset Utc(DateTimeOffset value) => value.ToUniversalTime();

        public static DateTimeOffset NotBefore(DateTimeOffset value, DateTimeOffset reference, string paramName) =>
            value < reference
                ? throw new ArgumentOutOfRangeException(paramName, value, $"Cannot be earlier than {reference:O}.")
                : value.ToUniversalTime();

        public static decimal Positive(decimal value, string paramName) =>
            value <= 0 ? throw new ArgumentOutOfRangeException(paramName, value, "Must be greater than zero.") : value;

        private static string MaxLength(string value, int maxLength, string paramName) =>
            value.Length > maxLength
                ? throw new ArgumentException($"Cannot exceed {maxLength} characters.", paramName)
                : value;
    }
}
