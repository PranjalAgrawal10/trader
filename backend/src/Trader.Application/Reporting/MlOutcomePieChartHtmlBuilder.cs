using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Trader.Application.Abstractions.Reporting;

namespace Trader.Application.Reporting;

/// <summary>Outcome pie placeholders for multipart HTML email: <c>&lt;img src="cid:…"&gt;</c> + legend (Bootstrap-like colors). Raster PNGs must be attached as <see cref="Trader.Application.Abstractions.Messaging.EmbeddedEmailImage"/>.</summary>
public static class MlOutcomePieChartHtmlBuilder
{
    public const string ColorCorrect = "#198754";
    public const string ColorWrong = "#dc3545";
    public const string ColorPending = "#6c757d";

    /// <summary>HTML fragment containing one labeled chart referencing an inline image by <paramref name="imageContentId"/>.</summary>
    public static string BuildChartSection(string title, string imageContentId, int correct, int wrong, int pending)
    {
        var enc = HtmlEncoder.Default;
        var safeTitle = string.IsNullOrWhiteSpace(title) ? "ML outcomes" : title.Trim();
        if (safeTitle.Length > 120)
            safeTitle = safeTitle[..117] + "…";

        var cid = string.IsNullOrWhiteSpace(imageContentId) ? "ml-pie" : imageContentId.Trim();

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

        var alt = enc.Encode(
            $"Outcome pie: correct {correct}, wrong {wrong}, pending {pending} (total {total}).");
        sb.Append("<div style=\"overflow-x:auto;-webkit-overflow-scrolling:touch;text-align:center;\">")
            .Append("<img src=\"cid:")
            .Append(enc.Encode(cid))
            .Append("\" width=\"")
            .Append(MlReportingConstants.MlPieImageWidth.ToString(CultureInfo.InvariantCulture))
            .Append("\" height=\"")
            .Append(MlReportingConstants.MlPieImageHeight.ToString(CultureInfo.InvariantCulture))
            .Append("\" alt=\"")
            .Append(alt)
            .Append("\" style=\"max-width:100%;height:auto;display:block;margin:0 auto;border:0;\" />")
            .Append("</div>\n");
        sb.Append("<table role=\"presentation\" style=\"margin-top:10px;font-size:14px;line-height:1.4;color:#212529;\">\n");
        AppendLegendRow(sb, enc, ColorCorrect, "Correct", correct);
        AppendLegendRow(sb, enc, ColorWrong, "Wrong", wrong);
        AppendLegendRow(sb, enc, ColorPending, "Pending", pending);
        sb.Append("</table>\n</section>");
        return sb.ToString();
    }

    /// <summary>Pie of best-of-three <strong>component</strong> votes (sum of up/down/neutral tallies across rows).</summary>
    public static string BuildDirectionVoteChartSection(string title, string imageContentId, int up, int down, int neutral)
    {
        var enc = HtmlEncoder.Default;
        var safeTitle = string.IsNullOrWhiteSpace(title) ? "Direction votes (best of 3)" : title.Trim();
        if (safeTitle.Length > 120)
            safeTitle = safeTitle[..117] + "…";

        var cid = string.IsNullOrWhiteSpace(imageContentId) ? "ml-pie-b3-dir" : imageContentId.Trim();

        var sb = new StringBuilder(2048)
            .Append("<section style=\"margin-bottom:28px;font-family:system-ui,Segoe UI,Roboto,Helvetica,sans-serif;\">")
            .Append("<h2 style=\"font-size:15px;font-weight:600;margin:0 0 12px;color:#212529;line-height:1.3;\">")
            .Append(enc.Encode(safeTitle))
            .Append("</h2>");

        var total = up + down + neutral;
        if (total <= 0)
            return sb
                .Append("<p style=\"margin:8px 0;color:#6c757d;font-size:14px;\">No best-of-three vote rows in this range.</p></section>")
                .ToString();

        var alt = enc.Encode($"Direction vote pie: up {up}, down {down}, neutral {neutral} (total {total} component votes).");
        sb.Append("<div style=\"overflow-x:auto;-webkit-overflow-scrolling:touch;text-align:center;\">")
            .Append("<img src=\"cid:")
            .Append(enc.Encode(cid))
            .Append("\" width=\"")
            .Append(MlReportingConstants.MlPieImageWidth.ToString(CultureInfo.InvariantCulture))
            .Append("\" height=\"")
            .Append(MlReportingConstants.MlPieImageHeight.ToString(CultureInfo.InvariantCulture))
            .Append("\" alt=\"")
            .Append(alt)
            .Append("\" style=\"max-width:100%;height:auto;display:block;margin:0 auto;border:0;\" />")
            .Append("</div>\n");
        sb.Append("<table role=\"presentation\" style=\"margin-top:10px;font-size:14px;line-height:1.4;color:#212529;\">\n");
        AppendLegendRow(sb, enc, ColorCorrect, "Up", up);
        AppendLegendRow(sb, enc, ColorWrong, "Down", down);
        AppendLegendRow(sb, enc, ColorPending, "Neutral", neutral);
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
            .Append("<p style=\"max-width:720px;margin:16px auto 0;color:#6c757d;font-size:12px;\">Outcome charts are inline PNG images. CSV spreadsheet is attached.</p>")
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
}
