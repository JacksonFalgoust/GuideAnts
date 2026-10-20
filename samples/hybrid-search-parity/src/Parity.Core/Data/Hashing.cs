using System.Security.Cryptography;

namespace Parity.Core.Data;

public static class Hashing
{
    public static string Sha256File(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
