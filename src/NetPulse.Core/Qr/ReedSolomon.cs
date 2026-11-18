namespace NetPulse.Core.Qr;

/// <summary>Reed-Solomon error correction over GF(2⁸) with the QR polynomial x⁸+x⁴+x³+x²+1.</summary>
internal static class ReedSolomon
{
    /// <summary>Generator polynomial of the given degree, highest coefficient (always 1) left out.</summary>
    public static byte[] Divisor(int degree)
    {
        if (degree is < 1 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(degree));
        }

        var result = new byte[degree];
        result[degree - 1] = 1;

        // Multiply by (x - α^i) for i = 0 … degree-1, with α = 2.
        byte root = 1;
        for (var i = 0; i < degree; i++)
        {
            for (var j = 0; j < result.Length; j++)
            {
                result[j] = Multiply(result[j], root);
                if (j + 1 < result.Length)
                {
                    result[j] ^= result[j + 1];
                }
            }

            root = Multiply(root, 0x02);
        }

        return result;
    }

    /// <summary>The error correction codewords: the remainder of data × x^degree divided by the divisor.</summary>
    public static byte[] Remainder(ReadOnlySpan<byte> data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (var b in data)
        {
            var factor = (byte)(b ^ result[0]);
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (var i = 0; i < result.Length; i++)
            {
                result[i] ^= Multiply(divisor[i], factor);
            }
        }

        return result;
    }

    /// <summary>Product in GF(2⁸) modulo 0x11D.</summary>
    public static byte Multiply(byte x, byte y)
    {
        var z = 0;
        for (var i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }

        return (byte)z;
    }
}
