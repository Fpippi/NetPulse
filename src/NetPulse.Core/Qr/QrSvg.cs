using System.Globalization;
using System.Text;

namespace NetPulse.Core.Qr;

/// <summary>Draws a <see cref="QrCode"/> as SVG: one path of unit squares, one unit per module.</summary>
public static class QrSvg
{
    /// <summary>Light modules required around the code by the standard.</summary>
    public const int QuietZone = 4;

    /// <summary>The <c>viewBox</c> for a code with the quiet zone, e.g. <c>0 0 29 29</c>.</summary>
    public static string ViewBox(QrCode qr) =>
        string.Create(CultureInfo.InvariantCulture, $"0 0 {qr.Size + QuietZone * 2} {qr.Size + QuietZone * 2}");

    /// <summary>
    /// Path data of the dark modules, offset by the quiet zone. Horizontal runs are merged into one
    /// rectangle each, which keeps the markup small and avoids hairline gaps between modules.
    /// </summary>
    public static string PathData(QrCode qr)
    {
        var path = new StringBuilder();
        for (var y = 0; y < qr.Size; y++)
        {
            for (var x = 0; x < qr.Size; x++)
            {
                if (!qr[x, y])
                {
                    continue;
                }

                var start = x;
                while (x + 1 < qr.Size && qr[x + 1, y])
                {
                    x++;
                }

                path.Append(CultureInfo.InvariantCulture,
                    $"M{start + QuietZone} {y + QuietZone}h{x - start + 1}v1h-{x - start + 1}z");
            }
        }

        return path.ToString();
    }

    /// <summary>A standalone SVG document: dark modules on a light square, as scanners expect.</summary>
    public static string Document(QrCode qr, string dark = "#000", string light = "#fff")
    {
        var box = qr.Size + QuietZone * 2;
        return string.Create(CultureInfo.InvariantCulture,
            $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {box} {box}" shape-rendering="crispEdges"><rect width="{box}" height="{box}" fill="{light}"/><path d="{PathData(qr)}" fill="{dark}"/></svg>""");
    }
}
