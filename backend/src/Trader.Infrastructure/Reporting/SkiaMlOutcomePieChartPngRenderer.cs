using SkiaSharp;
using Trader.Application.Abstractions.Reporting;

namespace Trader.Infrastructure.Reporting;

public sealed class SkiaMlOutcomePieChartPngRenderer : IMlOutcomePieChartPngRenderer
{
    public byte[] Render(int correct, int wrong, int pending, string? chartTitle = null)
    {
        const int w = 600;
        const int h = 440;
        var total = correct + wrong + pending;
        using var bitmap = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var titlePaint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true,
            TextSize = 22f,
            Typeface = SKTypeface.FromFamilyName("Arial"),
        };
        var title = string.IsNullOrWhiteSpace(chartTitle) ? "ML outcomes (favorites)" : chartTitle.Trim();
        if (title.Length > 96)
            title = title[..96] + "…";
        canvas.DrawText(title, 24f, 36f, titlePaint);

        if (total == 0)
        {
            using var p = new SKPaint { Color = SKColors.Gray, TextSize = 16f, IsAntialias = true };
            canvas.DrawText("No resolved rows for this period.", 24f, 80f, p);
            return Encode(bitmap);
        }

        var cx = w / 2f;
        var cy = h / 2f - 32f;
        var radius = Math.Min(w * 0.42f, h * 0.38f);
        float start = -90f;

        var slices = new (int count, SKColor color)[]
        {
            (correct, new SKColor(40, 167, 69)),
            (wrong, new SKColor(220, 53, 69)),
            (pending, new SKColor(108, 117, 125)),
        };

        foreach (var (count, color) in slices)
        {
            if (count <= 0)
                continue;
            var sweep = (float)(360.0 * count / total);
            using var path = new SKPath();
            path.MoveTo(cx, cy);
            path.AddArc(new SKRect(cx - radius, cy - radius, cx + radius, cy + radius), start, sweep);
            path.Close();
            using var paint = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
            canvas.DrawPath(path, paint);
            start += sweep;
        }

        using var legendPaint = new SKPaint { IsAntialias = true, TextSize = 15f, Color = SKColors.Black };
        // Stacked legend (not one horizontal row) so wide counts / fonts never clip canvas edges.
        const float lx = 40f;
        var ly = h - 100f;
        DrawLegendSwatch(canvas, lx, ly, new SKColor(40, 167, 69));
        canvas.DrawText($"Correct: {correct}", lx + 24f, ly + 14f, legendPaint);
        ly += 28f;
        DrawLegendSwatch(canvas, lx, ly, new SKColor(220, 53, 69));
        canvas.DrawText($"Wrong: {wrong}", lx + 24f, ly + 14f, legendPaint);
        ly += 28f;
        DrawLegendSwatch(canvas, lx, ly, new SKColor(108, 117, 125));
        canvas.DrawText($"Pending: {pending}", lx + 24f, ly + 14f, legendPaint);

        return Encode(bitmap);
    }

    private static void DrawLegendSwatch(SKCanvas canvas, float x, float y, SKColor color)
    {
        using var p = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawRect(x, y, 16f, 16f, p);
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using var img = SKImage.FromBitmap(bitmap);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
