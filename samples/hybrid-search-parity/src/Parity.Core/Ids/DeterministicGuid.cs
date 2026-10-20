using System.Security.Cryptography;
using System.Text;

namespace Parity.Core.Ids;

/// <summary>
/// Name-based (RFC 4122 version 5) GUIDs, so both databases hold identical, reproducible IDs.
/// </summary>
public static class DeterministicGuid
{
    /// <summary>
    /// Fixed namespace for every ID in this sample. Never change it: chunk IDs in data/ and
    /// known-differences.json depend on it.
    /// </summary>
    public static readonly Guid ParityNamespace = new("3d6f9a52-8b1e-4c47-9f0d-5a2e7c1b9e44");

    public static Guid Create(string name) => Create(ParityNamespace, name);

    public static Guid Create(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        SwapToNetworkOrder(namespaceBytes);
        var nameBytes = Encoding.UTF8.GetBytes(name);

        var input = new byte[namespaceBytes.Length + nameBytes.Length];
        namespaceBytes.CopyTo(input, 0);
        nameBytes.CopyTo(input, namespaceBytes.Length);

        var hash = SHA1.HashData(input);
        var guid = new byte[16];
        Array.Copy(hash, guid, 16);
        guid[6] = (byte)((guid[6] & 0x0F) | 0x50); // version 5
        guid[8] = (byte)((guid[8] & 0x3F) | 0x80); // RFC 4122 variant
        SwapToNetworkOrder(guid);
        return new Guid(guid);
    }

    // Guid.ToByteArray stores the first three fields little-endian; RFC 4122 hashes them big-endian.
    private static void SwapToNetworkOrder(byte[] b)
    {
        (b[0], b[3]) = (b[3], b[0]);
        (b[1], b[2]) = (b[2], b[1]);
        (b[4], b[5]) = (b[5], b[4]);
        (b[6], b[7]) = (b[7], b[6]);
    }
}
