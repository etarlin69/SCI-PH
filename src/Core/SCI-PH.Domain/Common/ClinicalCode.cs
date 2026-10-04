using SCI_PH.Domain.Constants;

namespace SCI_PH.Domain.Common
{
    // Optional coded concept (for example ICD-10). A code is meaningless without its code system.
    internal static class ClinicalCode
    {
        public static (string? CodeSystem, string? Code) Optional(string? codeSystem, string? code)
        {
            var system = Guard.OptionalText(codeSystem, FieldLengths.CodeSystem, nameof(codeSystem))?.ToUpperInvariant();
            var value = Guard.OptionalText(code, FieldLengths.Code, nameof(code))?.ToUpperInvariant();
            return value is not null && system is null
                ? throw new ArgumentException("A code requires its code system.", nameof(codeSystem))
                : (system, value);
        }
    }
}
