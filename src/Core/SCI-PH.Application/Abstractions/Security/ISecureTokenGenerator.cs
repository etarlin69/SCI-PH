namespace SCI_PH.Application.Abstractions.Security
{
    // Opaque one-time secrets (invitation tokens, device keys). Only the hash is ever persisted.
    public interface ISecureTokenGenerator
    {
        string Generate(int byteCount);

        string Hash(string value);

        // Constant-time comparison of the value hash with a stored hash.
        bool Matches(string value, string expectedHash);
    }
}
