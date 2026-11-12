using System.Text.RegularExpressions;

namespace SCI_PH.IntegrationTests.Security.Support
{
    // Error responses carry a ProblemDetails "traceId" (random hex). A test that checks a body never contains a short number
    // (a fingerprint slot such as "7319") could find those digits inside it by chance: it masks the trace id first. The rule
    // being tested is unchanged.
    internal static partial class RandomIds
    {
        public static string Mask(string text) => TraceId().Replace(text, "\"traceId\":\"<id>\"");

        [GeneratedRegex("\"traceId\"\\s*:\\s*\"[^\"]*\"")]
        private static partial Regex TraceId();
    }
}
