using System.Buffers.Binary;

namespace Parity.Core.Data;

/// <summary>Raw little-endian float32 vectors, Dimensions values each, back to back.</summary>
public static class VectorFile
{
    public const int Dimensions = 1536;

    public static void Write(string path, IReadOnlyList<float[]> vectors)
    {
        for (var i = 0; i < vectors.Count; i++)
        {
            Validate(vectors[i], i);
        }

        var bytes = new byte[(long)vectors.Count * Dimensions * 4];
        for (var i = 0; i < vectors.Count; i++)
        {
            for (var d = 0; d < Dimensions; d++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(((i * Dimensions) + d) * 4, 4), vectors[i][d]);
            }
        }

        File.WriteAllBytes(path, bytes);
    }

    public static float[][] Read(string path, int expectedCount)
    {
        var bytes = File.ReadAllBytes(path);
        var expectedLength = (long)expectedCount * Dimensions * 4;
        if (bytes.Length != expectedLength)
        {
            throw new DataFileException(
                $"{Path.GetFileName(path)} has {bytes.Length} bytes; expected {expectedLength} ({expectedCount} vectors × {Dimensions} × 4).");
        }

        var result = new float[expectedCount][];
        for (var i = 0; i < expectedCount; i++)
        {
            var vector = new float[Dimensions];
            for (var d = 0; d < Dimensions; d++)
            {
                vector[d] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(((i * Dimensions) + d) * 4, 4));
            }

            result[i] = vector;
        }

        return result;
    }

    public static void Validate(float[] vector, int index)
    {
        if (vector.Length != Dimensions)
        {
            throw new DataFileException($"Vector {index} has {vector.Length} dimensions; expected {Dimensions}.");
        }

        for (var d = 0; d < vector.Length; d++)
        {
            if (!float.IsFinite(vector[d]))
            {
                throw new DataFileException($"Vector {index} has a non-finite value at dimension {d}.");
            }
        }
    }
}
