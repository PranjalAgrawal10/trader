using SkiaSharp;
using Trader.Application.Abstractions.Reporting;

namespace Trader.Infrastructure.Reporting;

public sealed class SkiaMlOutcomePieChartPngRenderer : IMlOutcomePieChartPngRenderer
{
    public byte[] Render(int correct, int wrong, int pending, string? chartTitle = null)
    {
        const int w = 520;
        const int h = 420;
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
        var cy = h / 2f - 10f;
        var radius = Math.Min(w, h) / 2f - 56f;
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
        var ly = h - 78f;
        DrawLegendSwatch(canvas, 32f, ly, new SKColor(40, 167, 69));
        canvas.DrawText($"Correct: {correct}", 56f, ly + 14f, legendPaint);
        DrawLegendSwatch(canvas, 180f, ly, new SKColor(220, 53, 69));
        canvas.DrawText($"Wrong: {wrong}", 204f, ly + 14f, legendPaint);
        DrawLegendSwatch(canvas, 312f, ly, new SKColor(108, 117, 125));
        canvas.DrawText($"Pending: {pending}", 336f, ly + 14f, legendPaint);

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
