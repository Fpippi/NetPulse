using System.Text;

namespace NetPulse.Core.Qr;

/// <summary>How much of a QR code can be damaged and still be read.</summary>
public enum QrErrorCorrection
{
    /// <summary>About 7 % of the code can be restored.</summary>
    Low,

    /// <summary>About 15 %: the usual choice for a code shown on a screen.</summary>
    Medium,

    /// <summary>About 25 %.</summary>
    Quartile,

    /// <summary>About 30 %.</summary>
    High,
}

/// <summary>
/// A QR code (ISO/IEC 18004, model 2) encoded in byte mode, versions 1 to 10: up to 271 bytes at
/// <see cref="QrErrorCorrection.Low"/>, 213 at <see cref="QrErrorCorrection.Medium"/>, which is plenty
/// for a URL. Written from the standard so the dashboard needs no library and no online generator.
/// </summary>
public sealed class QrCode
{
    public const int MinVersion = 1;
    public const int MaxVersion = 10;

    // Per version (index 0 unused) and error correction level, from table 9 of the standard.
    private static readonly int[][] EccCodewordsPerBlock =
    [
        [-1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18],  // Low
        [-1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26], // Medium
        [-1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24], // Quartile
        [-1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28], // High
    ];

    private static readonly int[][] ErrorCorrectionBlocks =
    [
        [-1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4],
        [-1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5],
        [-1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8],
        [-1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8],
    ];

    private readonly bool[,] _modules;   // [y, x], true = dark
    private readonly bool[,] _isFunction;

    private QrCode(int version, QrErrorCorrection ecc)
    {
        Version = version;
        ErrorCorrection = ecc;
        Size = version * 4 + 17;
        _modules = new bool[Size, Size];
        _isFunction = new bool[Size, Size];
    }

    /// <summary>1 to 10; the size grows by 4 modules per version.</summary>
    public int Version { get; }

    /// <summary>Modules per side (21 for version 1), without the quiet zone.</summary>
    public int Size { get; }

    public QrErrorCorrection ErrorCorrection { get; }

    /// <summary>The mask pattern (0 to 7) that was applied.</summary>
    public int Mask { get; private set; }

    /// <summary>True if the module at column <paramref name="x"/>, row <paramref name="y"/> is dark.</summary>
    public bool this[int x, int y] => x >= 0 && x < Size && y >= 0 && y < Size && _modules[y, x];

    /// <summary>Largest UTF-8 payload in bytes that fits at the given level.</summary>
    public static int Capacity(QrErrorCorrection ecc) => DataCodewords(MaxVersion, ecc) - 3;

    /// <summary>Encodes UTF-8 text in the smallest version that fits.</summary>
    /// <exception cref="ArgumentException">The text is too long for a version 10 code.</exception>
    public static QrCode Encode(string text, QrErrorCorrection ecc = QrErrorCorrection.Medium) =>
        Encode(Encoding.UTF8.GetBytes(text), ecc, mask: null);

    /// <summary>Encodes bytes; <paramref name="mask"/> forces a mask pattern instead of the best one.</summary>
    public static QrCode Encode(ReadOnlySpan<byte> data, QrErrorCorrection ecc, int? mask = null)
    {
        if (mask is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(mask), "The mask must be 0 to 7.");
        }

        var version = MinVersion;
        while (version <= MaxVersion && BitsNeeded(data.Length, version) > DataCodewords(version, ecc) * 8)
        {
            version++;
        }

        if (version > MaxVersion)
        {
            throw new ArgumentException(
                $"{data.Length} bytes do not fit in a QR code (at most {Capacity(ecc)} at this level).", nameof(data));
        }

        var qr = new QrCode(version, ecc);
        qr.DrawFunctionPatterns();
        qr.DrawCodewords(qr.AddErrorCorrection(qr.DataBytes(data)));
        qr.ApplyBestMask(mask);
        return qr;
    }

    // --- Data ---------------------------------------------------------------

    private static int CountBits(int version) => version <= 9 ? 8 : 16;

    private static int BitsNeeded(int length, int version) => 4 + CountBits(version) + length * 8;

    /// <summary>Modules left for data and error correction once the function patterns are drawn.</summary>
    private static int RawDataModules(int version)
    {
        var result = (16 * version + 128) * version + 64;
        if (version >= 2)
        {
            var alignments = version / 7 + 2;
            result -= (25 * alignments - 10) * alignments - 55;
            if (version >= 7)
            {
                result -= 36; // two version information blocks
            }
        }

        return result;
    }

    private static int DataCodewords(int version, QrErrorCorrection ecc) =>
        RawDataModules(version) / 8
        - EccCodewordsPerBlock[(int)ecc][version] * ErrorCorrectionBlocks[(int)ecc][version];

    /// <summary>Mode indicator, length, the bytes, terminator and padding: exactly the data codewords.</summary>
    private byte[] DataBytes(ReadOnlySpan<byte> data)
    {
        var bits = new BitBuffer();
        bits.Append(0b0100, 4); // byte mode
        bits.Append(data.Length, CountBits(Version));
        foreach (var b in data)
        {
            bits.Append(b, 8);
        }

        var capacity = DataCodewords(Version, ErrorCorrection) * 8;
        bits.Append(0, Math.Min(4, capacity - bits.Count));
        bits.Append(0, (8 - bits.Count % 8) % 8);
        for (var pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11)
        {
            bits.Append(pad, 8);
        }

        return bits.ToBytes();
    }

    /// <summary>Splits the data into blocks, adds Reed-Solomon codewords and interleaves them.</summary>
    private byte[] AddErrorCorrection(byte[] data)
    {
        var blockCount = ErrorCorrectionBlocks[(int)ErrorCorrection][Version];
        var eccLength = EccCodewordsPerBlock[(int)ErrorCorrection][Version];
        var rawCodewords = RawDataModules(Version) / 8;
        var shortBlocks = blockCount - rawCodewords % blockCount;
        var shortDataLength = rawCodewords / blockCount - eccLength;

        var divisor = ReedSolomon.Divisor(eccLength);
        var dataBlocks = new byte[blockCount][];
        var eccBlocks = new byte[blockCount][];
        for (int i = 0, offset = 0; i < blockCount; i++)
        {
            var length = shortDataLength + (i < shortBlocks ? 0 : 1);
            dataBlocks[i] = data[offset..(offset + length)];
            eccBlocks[i] = ReedSolomon.Remainder(dataBlocks[i], divisor);
            offset += length;
        }

        var result = new List<byte>(rawCodewords);
        for (var i = 0; i <= shortDataLength; i++)
        {
            foreach (var block in dataBlocks)
            {
                if (i < block.Length)
                {
                    result.Add(block[i]);
                }
            }
        }

        for (var i = 0; i < eccLength; i++)
        {
            foreach (var block in eccBlocks)
            {
                result.Add(block[i]);
            }
        }

        return [.. result];
    }

    // --- Drawing ------------------------------------------------------------

    private void SetFunction(int x, int y, bool dark)
    {
        _modules[y, x] = dark;
        _isFunction[y, x] = true;
    }

    private void DrawFunctionPatterns()
    {
        for (var i = 0; i < Size; i++)
        {
            SetFunction(6, i, i % 2 == 0);
            SetFunction(i, 6, i % 2 == 0);
        }

        DrawFinder(3, 3);
        DrawFinder(Size - 4, 3);
        DrawFinder(3, Size - 4);

        var positions = AlignmentPositions(Version);
        var last = positions.Length - 1;
        for (var i = 0; i < positions.Length; i++)
        {
            for (var j = 0; j < positions.Length; j++)
            {
                // Skip the three corners taken by the finder patterns.
                if (!((i == 0 && j == 0) || (i == 0 && j == last) || (i == last && j == 0)))
                {
                    DrawAlignment(positions[i], positions[j]);
                }
            }
        }

        DrawFormatBits(0); // reserves the area; redrawn with the real mask later
        DrawVersion();
    }

    /// <summary>Centre coordinates of the alignment patterns on each axis.</summary>
    internal static int[] AlignmentPositions(int version)
    {
        if (version == 1)
        {
            return [];
        }

        var count = version / 7 + 2;
        var step = (version * 4 + count * 2 + 1) / (count * 2 - 2) * 2;
        var result = new int[count];
        result[0] = 6;
        for (int i = count - 1, pos = version * 4 + 10; i >= 1; i--, pos -= step)
        {
            result[i] = pos;
        }

        return result;
    }

    private void DrawFinder(int cx, int cy)
    {
        for (var dy = -4; dy <= 4; dy++)
        {
            for (var dx = -4; dx <= 4; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x >= 0 && x < Size && y >= 0 && y < Size)
                {
                    var distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    SetFunction(x, y, distance != 2 && distance != 4);
                }
            }
        }
    }

    private void DrawAlignment(int cx, int cy)
    {
        for (var dy = -2; dy <= 2; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                SetFunction(cx + dx, cy + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
        }
    }

    /// <summary>The 15-bit format word: level and mask, BCH-protected, XOR-masked.</summary>
    internal static int FormatBits(QrErrorCorrection ecc, int mask)
    {
        var levelBits = ecc switch
        {
            QrErrorCorrection.Low => 1,
            QrErrorCorrection.Medium => 0,
            QrErrorCorrection.Quartile => 3,
            _ => 2,
        };

        var data = levelBits << 3 | mask;
        var rem = data;
        for (var i = 0; i < 10; i++)
        {
            rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        }

        return (data << 10 | rem) ^ 0x5412;
    }

    private void DrawFormatBits(int mask)
    {
        var bits = FormatBits(ErrorCorrection, mask);
        bool Bit(int i) => ((bits >> i) & 1) != 0;

        // Around the top-left finder.
        for (var i = 0; i <= 5; i++)
        {
            SetFunction(8, i, Bit(i));
        }

        SetFunction(8, 7, Bit(6));
        SetFunction(8, 8, Bit(7));
        SetFunction(7, 8, Bit(8));
        for (var i = 9; i < 15; i++)
        {
            SetFunction(14 - i, 8, Bit(i));
        }

        // The copy split between the other two finders.
        for (var i = 0; i < 8; i++)
        {
            SetFunction(Size - 1 - i, 8, Bit(i));
        }

        for (var i = 8; i < 15; i++)
        {
            SetFunction(8, Size - 15 + i, Bit(i));
        }

        SetFunction(8, Size - 8, true); // the dark module
    }

    /// <summary>The 18-bit version word (versions 7 and up), BCH-protected.</summary>
    internal static int VersionBits(int version)
    {
        var rem = version;
        for (var i = 0; i < 12; i++)
        {
            rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
        }

        return version << 12 | rem;
    }

    private void DrawVersion()
    {
        if (Version < 7)
        {
            return;
        }

        var bits = VersionBits(Version);
        for (var i = 0; i < 18; i++)
        {
            var dark = ((bits >> i) & 1) != 0;
            int a = Size - 11 + i % 3, b = i / 3;
            SetFunction(a, b, dark);
            SetFunction(b, a, dark);
        }
    }

    /// <summary>Places the codewords in the two-module-wide zigzag, right to left.</summary>
    private void DrawCodewords(byte[] codewords)
    {
        var i = 0;
        for (var right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6)
            {
                right = 5; // skip the vertical timing pattern
            }

            for (var vertical = 0; vertical < Size; vertical++)
            {
                for (var j = 0; j < 2; j++)
                {
                    var x = right - j;
                    var upward = ((right + 1) & 2) == 0;
                    var y = upward ? Size - 1 - vertical : vertical;
                    if (!_isFunction[y, x] && i < codewords.Length * 8)
                    {
                        _modules[y, x] = ((codewords[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                        i++;
                    }

                    // Remainder bits (if any) stay light.
                }
            }
        }
    }

    // --- Masking ------------------------------------------------------------

    private static bool MaskHit(int mask, int x, int y) => mask switch
    {
        0 => (x + y) % 2 == 0,
        1 => y % 2 == 0,
        2 => x % 3 == 0,
        3 => (x + y) % 3 == 0,
        4 => (x / 3 + y / 2) % 2 == 0,
        5 => x * y % 2 + x * y % 3 == 0,
        6 => (x * y % 2 + x * y % 3) % 2 == 0,
        _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
    };

    /// <summary>XORs a mask over the data modules; applying it twice undoes it.</summary>
    private void ApplyMask(int mask)
    {
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (!_isFunction[y, x] && MaskHit(mask, x, y))
                {
                    _modules[y, x] = !_modules[y, x];
                }
            }
        }
    }

    private void ApplyBestMask(int? forced)
    {
        var best = forced ?? 0;
        if (forced is null)
        {
            var lowest = int.MaxValue;
            for (var mask = 0; mask < 8; mask++)
            {
                ApplyMask(mask);
                DrawFormatBits(mask);
                var penalty = Penalty();
                if (penalty < lowest)
                {
                    lowest = penalty;
                    best = mask;
                }

                ApplyMask(mask);
            }
        }

        Mask = best;
        ApplyMask(best);
        DrawFormatBits(best);
    }

    /// <summary>The penalty score of the standard (lower is easier to scan).</summary>
    internal int Penalty()
    {
        var penalty = 0;

        // Rule 1 (runs of five or more) and rule 3 (finder-like patterns), rows then columns.
        for (var line = 0; line < Size; line++)
        {
            penalty += LinePenalty(i => _modules[line, i]);
            penalty += LinePenalty(i => _modules[i, line]);
        }

        // Rule 2: 2×2 blocks of one colour.
        for (var y = 0; y < Size - 1; y++)
        {
            for (var x = 0; x < Size - 1; x++)
            {
                var c = _modules[y, x];
                if (c == _modules[y, x + 1] && c == _modules[y + 1, x] && c == _modules[y + 1, x + 1])
                {
                    penalty += 3;
                }
            }
        }

        // Rule 4: share of dark modules away from 50 %, in steps of 5 %.
        var dark = 0;
        foreach (var module in _modules)
        {
            dark += module ? 1 : 0;
        }

        var total = Size * Size;
        var k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
        return penalty + k * 10;
    }

    /// <summary>
    /// Rules 1 and 3 for one row or column. Finder-like patterns (dark-light-dark-light-dark in
    /// 1:1:3:1:1, with four light modules on one side) are found from the run lengths, with the
    /// area outside the code counted as light, as in the reference implementation.
    /// </summary>
    private int LinePenalty(Func<int, bool> at)
    {
        var penalty = 0;
        var history = new int[7];
        var runDark = false;
        var run = 0;
        for (var i = 0; i < Size; i++)
        {
            if (at(i) == runDark)
            {
                run++;
                penalty += run == 5 ? 3 : run > 5 ? 1 : 0;
                continue;
            }

            AddRun(run, history);
            if (!runDark)
            {
                penalty += FinderLikePatterns(history) * 40;
            }

            runDark = at(i);
            run = 1;
        }

        // Close the line: the last run, then the light area beyond the edge.
        if (runDark)
        {
            AddRun(run, history);
            run = 0;
        }

        AddRun(run + Size, history);
        return penalty + FinderLikePatterns(history) * 40;
    }

    /// <summary>Pushes a run length; the first run of a line also gets the light area before the edge.</summary>
    private void AddRun(int length, int[] history)
    {
        if (history[0] == 0)
        {
            length += Size;
        }

        Array.Copy(history, 0, history, 1, history.Length - 1);
        history[0] = length;
    }

    /// <summary>How many finder-like patterns end at the latest light run (0, 1 or 2).</summary>
    private static int FinderLikePatterns(int[] h)
    {
        var n = h[1];
        var core = n > 0 && h[2] == n && h[3] == n * 3 && h[4] == n && h[5] == n;
        return (core && h[0] >= n * 4 && h[6] >= n ? 1 : 0) + (core && h[6] >= n * 4 && h[0] >= n ? 1 : 0);
    }

    private sealed class BitBuffer
    {
        private readonly List<bool> _bits = [];

        public int Count => _bits.Count;

        public void Append(int value, int length)
        {
            for (var i = length - 1; i >= 0; i--)
            {
                _bits.Add(((value >> i) & 1) != 0);
            }
        }

        public byte[] ToBytes()
        {
            var bytes = new byte[_bits.Count / 8];
            for (var i = 0; i < _bits.Count; i++)
            {
                if (_bits[i])
                {
                    bytes[i >> 3] |= (byte)(0x80 >> (i & 7));
                }
            }

            return bytes;
        }
    }
}
