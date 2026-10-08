using System.Text.Json.Serialization;

namespace SCI_PH.Application.Common.Serialization
{
    // Clinical contracts exchange enums as their C# member names ("Emergency", "InProgress"). Applied per property on
    // clinical DTOs only, so the global JSON options (and the Auth/Admin contracts) keep their current behavior.
    // Numbers are rejected: a client cannot send a value that is not part of the published names.
    public sealed class TextEnumConverter : JsonStringEnumConverter
    {
        public TextEnumConverter() : base(namingPolicy: null, allowIntegerValues: false) { }
    }
}
