using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Trader.Application.Abstractions.Reporting;

namespace Trader.Infrastructure.Reporting;

/// <summary>
/// Raster pie charts for email <c>&lt;img src="cid:…"&gt;</c> — reliably rendered in Outlook, Gmail, and Apple Mail
/// (unlike SVG / CSS pies, which many clients strip or break).
/// </summary>
public sealed class ImageSharpMlOutcomePieChartPngGenerator : IMlOutcomePieChartPngGenerator
{
    private static readonly Color CorrectRgb = Color.FromRgb(25, 135, 84);
    private static readonly Color WrongRgb = Color.FromRgb(220, 53, 69);
    private static readonly Color PendingRgb = Color.FromRgb(108, 117, 125);

    public byte[] RenderPng(int correct, int wrong, int pending)
    {
        const int w = MlReportingConstants.MlPieImageWidth;
        const int h = MlReportingConstants.MlPieImageHeight;
        using var image = new Image<Rgba32>(w, h, Color.White);

        var total = correct + wrong + pending;
        if (total > 0)
        {
            var center = new PointF(w / 2f, h / 2f - 8f);
            var radius = Math.Min(w * 0.42f, h * 0.38f);
            float startDeg = -90f;

            DrawSlice(image, center, radius, correct, total, CorrectRgb, ref startDeg);
            DrawSlice(image, center, radius, wrong, total, WrongRgb, ref startDeg);
            DrawSlice(image, center, radius, pending, total, PendingRgb, ref startDeg);
        }

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static void DrawSlice(
        Image<Rgba32> image,
        PointF center,
        float radius,
        int count,
        int total,
        Color color,
        ref float startDeg)
    {
        if (count <= 0)
            return;

        var sweepDeg = 360f * count / total;
        var verts = BuildPieVertices(center, radius, startDeg, sweepDeg);
        startDeg += sweepDeg;
        if (verts.Length < 3)
            return;

        image.Mutate(ctx => ctx.FillPolygon(color, verts));
    }

    /// <remarks>Allocated per wedge; small bounded size (few dozen points).</remarks>
    private static PointF[] BuildPieVertices(PointF center, float radius, float startDeg, float sweepDeg)
    {
        if (sweepDeg <= 0)
            return Array.Empty<PointF>();

        var maxPerimeter = 90;
        var n = Math.Clamp((int)Math.Ceiling(sweepDeg / 4f), 4, maxPerimeter);
        var verts = new PointF[n + 2];
        verts[0] = center;
        for (var i = 0; i <= n; i++)
        {
            var t = startDeg + sweepDeg * (i / (float)n);
            var rad = t * ((float)Math.PI / 180f);
            verts[i + 1] = new PointF(
                center.X + radius * MathF.Cos(rad),
                center.Y + radius * MathF.Sin(rad));
        }

        return verts;
    }
}
