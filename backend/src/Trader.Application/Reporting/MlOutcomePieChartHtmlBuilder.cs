using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;

namespace Trader.Application.Reporting;

/// <summary>Inline SVG outcome pies for multipart HTML email (Bootstrap-like colors).</summary>
public static class MlOutcomePieChartHtmlBuilder
{
    public const string ColorCorrect = "#198754";
    public const string ColorWrong = "#dc3545";
    public const string ColorPending = "#6c757d";

    /// <summary>HTML fragment containing one labeled chart.</summary>
    public static string BuildChartSection(string title, int correct, int wrong, int pending)
    {
        var enc = HtmlEncoder.Default;
        var safeTitle = string.IsNullOrWhiteSpace(title) ? "ML outcomes" : title.Trim();
        if (safeTitle.Length > 120)
            safeTitle = safeTitle[..117] + "…";

        var sb = new StringBuilder(2048)
            .Append("<section style=\"margin-bottom:28px;font-family:system-ui,Segoe UI,Roboto,Helvetica,sans-serif;\">")
            .Append("<h2 style=\"font-size:15px;font-weight:600;margin:0 0 12px;color:#212529;line-height:1.3;\">")
            .Append(enc.Encode(safeTitle))
            .Append("</h2>");

        var total = correct + wrong + pending;
        if (total <= 0)
            return sb
                .Append("<p style=\"margin:8px 0;color:#6c757d;font-size:14px;\">No rows for this chart.</p></section>")
                .ToString();

        sb.Append(BuildSvgPieMarkup(correct, wrong, pending));
        sb.Append("<table role=\"presentation\" style=\"margin-top:10px;font-size:14px;line-height:1.4;color:#212529;\">\n");
        AppendLegendRow(sb, enc, ColorCorrect, "Correct", correct);
        AppendLegendRow(sb, enc, ColorWrong, "Wrong", wrong);
        AppendLegendRow(sb, enc, ColorPending, "Pending", pending);
        sb.Append("</table>\n</section>");
        return sb.ToString();
    }

    /// <summary>Minimal HTML5 wrapper for multipart alternative.</summary>
    public static string WrapHtmlDocument(IEnumerable<string> sectionFragments)
    {
        var sb = new StringBuilder(4096)
            .Append("<!DOCTYPE html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"></head>\n<body style=\"margin:16px;background:#fafafa;color:#212529;\">")
            .Append("<div style=\"max-width:720px;margin:0 auto;background:#ffffff;border:1px solid #dee2e6;border-radius:8px;padding:20px 24px;\">");

        foreach (var frag in sectionFragments)
            sb.Append(frag);

        sb.Append("</div>")
            .Append("<p style=\"max-width:720px;margin:16px auto 0;color:#6c757d;font-size:12px;\">CSV spreadsheet is attached.</p>")
            .Append("</body></html>");
        return sb.ToString();
    }

    private static void AppendLegendRow(StringBuilder sb, HtmlEncoder enc, string color, string label, int value)
    {
        sb.Append("<tr><td style=\"vertical-align:middle;padding:4px 10px 4px 0;\">")
            .Append("<span style=\"display:inline-block;width:14px;height:14px;background:")
            .Append(color)
            .Append(";border-radius:2px\"></span>")
            .Append("</td><td>")
            .Append(enc.Encode(label))
            .Append(": <strong>")
            .Append(value.ToString(CultureInfo.InvariantCulture))
            .Append("</strong></td></tr>\n");
    }

    internal static string BuildSvgPieMarkup(int correct, int wrong, int pending)
    {
        const double cx = 110;
        const double cy = 110;
        const double r = 92;
        var total = correct + wrong + pending;
        if (total <= 0)
            return string.Empty;

        var slices = new (int Count, string Color)[]
        {
            (correct, ColorCorrect),
            (wrong, ColorWrong),
            (pending, ColorPending),
        };

        double startRad = -Math.PI / 2;
        var pathSb = new StringBuilder();
        foreach (var (count, fill) in slices)
        {
            if (count <= 0)
                continue;

            var sweepRad = 2 * Math.PI * count / total;
            var endRad = startRad + sweepRad;
            AppendPieSlicePath(pathSb, cx, cy, r, startRad, endRad, fill);
            startRad = endRad;
        }

        pathSb.Insert(
            0,
            "<div style=\"overflow-x:auto;-webkit-overflow-scrolling:touch;\">"
                + "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 220 220\" width=\"440\" height=\"440\" "
                + "role=\"img\" aria-label=\"Outcome breakdown pie chart\" style=\"max-width:100%;height:auto;\">");
        pathSb.Append("</svg></div>\n");
        return pathSb.ToString();
    }

    private static void AppendPieSlicePath(StringBuilder sb, double cx, double cy, double r, double startRad, double endRad, string fill)
    {
        var x1 = cx + r * Math.Cos(startRad);
        var y1 = cy + r * Math.Sin(startRad);
        var x2 = cx + r * Math.Cos(endRad);
        var y2 = cy + r * Math.Sin(endRad);

        var sweepDeg = Math.Abs(endRad - startRad) * (180 / Math.PI);
        var largeArc = sweepDeg > 180 ? 1 : 0;

        sb.Append("<path fill=\"").Append(fill).Append("\" stroke=\"#ffffff\" stroke-width=\"2\" d=\"M ")
            .Append(cx.ToString("F4", CultureInfo.InvariantCulture))
            .Append(',')
            .Append(cy.ToString("F4", CultureInfo.InvariantCulture))
            .Append(" L ")
            .Append(x1.ToString("F4", CultureInfo.InvariantCulture))
            .Append(',')
            .Append(y1.ToString("F4", CultureInfo.InvariantCulture))
            .Append(" A ")
            .Append(r.ToString("F4", CultureInfo.InvariantCulture))
            .Append(',')
            .Append(r.ToString("F4", CultureInfo.InvariantCulture))
            .Append(' ')
            .Append("0 ")
            .Append(largeArc.ToString(CultureInfo.InvariantCulture))
            .Append(" 1 ")
            .Append(x2.ToString("F4", CultureInfo.InvariantCulture))
            .Append(',')
            .Append(y2.ToString("F4", CultureInfo.InvariantCulture))
            .Append(" Z\" />\n");
    }
}
