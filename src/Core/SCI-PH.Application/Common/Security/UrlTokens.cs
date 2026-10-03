using System.Buffers.Text;
using System.Text;

namespace SCI_PH.Application.Common.Security
{
    // Identity tokens contain characters that are unsafe in URLs; they travel Base64Url-encoded.
    public static class UrlTokens
    {
        public static string Encode(string token) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(token));

        // Returns null for malformed input instead of throwing.
        public static string? Decode(string encoded)
        {
            try
            {
                return Encoding.UTF8.GetString(Base64Url.DecodeFromChars(encoded));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        public static string AppendQuery(string baseUrl, params (string Name, string Value)[] parameters) =>
            baseUrl + "?" + string.Join("&", parameters.Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value)}"));
    }
}
