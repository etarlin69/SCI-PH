using System.Globalization;

namespace SCI_PH.Persistence.Configurations
{
    // Builds single-line SQL for CHECK constraints and index filters from property names and enum values.
    // Single-line text keeps the model identical on LF and CRLF checkouts (no false pending model changes),
    // and enum members avoid magic numbers. PostgreSQL treats NULL comparisons as "unknown", which a CHECK accepts.
    internal static class Sql
    {
        public static string Column(string propertyName) => $"\"{propertyName}\"";

        public static string Value<TEnum>(TEnum value) where TEnum : struct, Enum =>
            Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

        public static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);

        public static string Text(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

        public static string Is<TEnum>(string propertyName, TEnum value) where TEnum : struct, Enum =>
            $"{Column(propertyName)} = {Value(value)}";

        public static string In<TEnum>(string propertyName, IEnumerable<TEnum> values) where TEnum : struct, Enum =>
            $"{Column(propertyName)} IN ({string.Join(", ", values.Select(Value))})";

        public static string IsNull(string propertyName) => $"{Column(propertyName)} IS NULL";

        public static string IsNotNull(string propertyName) => $"{Column(propertyName)} IS NOT NULL";

        public static string IsTrue(string propertyName) => Column(propertyName);

        public static string NotBlank(string propertyName) => $"length(btrim({Column(propertyName)})) > 0";

        public static string NotBefore(string laterProperty, string earlierProperty) =>
            $"{Column(laterProperty)} >= {Column(earlierProperty)}";

        public static string After(string laterProperty, string earlierProperty) =>
            $"{Column(laterProperty)} > {Column(earlierProperty)}";

        public static string GreaterThanZero(string propertyName) => $"{Column(propertyName)} > 0";

        public static string BothOrNeither(string firstProperty, string secondProperty) =>
            $"({IsNull(firstProperty)}) = ({IsNull(secondProperty)})";

        public static string ExactlyOne(string firstProperty, string secondProperty) =>
            $"({IsNull(firstProperty)}) <> ({IsNull(secondProperty)})";

        public static string Implies(string condition, string consequence) => $"NOT ({condition}) OR ({consequence})";

        public static string All(params string[] conditions) => string.Join(" AND ", conditions.Select(c => $"({c})"));

        public static string Any(params string[] conditions) => string.Join(" OR ", conditions.Select(c => $"({c})"));
    }
}
