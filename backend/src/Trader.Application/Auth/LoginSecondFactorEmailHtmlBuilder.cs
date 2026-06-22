using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;

namespace Trader.Application.Auth;

/// <summary>Multipart HTML for login second-factor (email OTP) messages.</summary>
internal static class LoginSecondFactorEmailHtmlBuilder
{
    private const string BrandBlue = "#0d6efd";
    private const string Ink = "#212529";
    private const string Muted = "#6c757d";
    private const string Surface = "#f8f9fa";
    private const string Border = "#dee2e6";

    /// <summary>
    /// Table-based layout for broad client support. OTP is shown once in a large selectable block
    /// (HTML mail cannot use clipboard APIs).
    /// </summary>
    public static string Build(string plainCode, int expiryMinutes)
    {
        var enc = HtmlEncoder.Default;
        var code = enc.Encode(plainCode);
        var expiry = enc.Encode(expiryMinutes.ToString(CultureInfo.InvariantCulture));
        var spacedCode = FormatSpacedOtp(plainCode, enc);

        var sb = new StringBuilder(4096);
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<title>Trader sign-in code</title></head>")
            .Append("<body style=\"margin:0;padding:0;background:#e9ecef;\">")
            .Append("<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;color:transparent;\">")
            .Append("Your Trader sign-in code is ")
            .Append(code)
            .Append(". Expires in ")
            .Append(expiry)
            .Append(" minutes.</div>")
            .Append("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\"")
            .Append(" style=\"background:#e9ecef;border-collapse:collapse;\">")
            .Append("<tr><td align=\"center\" style=\"padding:32px 16px;\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\"")
            .Append(" style=\"max-width:520px;border-collapse:separate;border-spacing:0;background:#ffffff;")
            .Append("border:1px solid ")
            .Append(Border)
            .Append(";border-radius:16px;overflow:hidden;box-shadow:0 4px 24px rgba(33,37,41,0.08);\">");

        AppendHeader(sb);
        AppendBody(sb, enc, code, spacedCode, expiryMinutes);
        AppendFooter(sb);

        sb.Append("</table></td></tr></table></body></html>");
        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb)
    {
        sb.Append("<tr><td style=\"background:")
            .Append(BrandBlue)
            .Append(";padding:28px 32px 24px;text-align:left;\">")
            .Append("<p style=\"margin:0 0 6px;font-family:system-ui,-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;")
            .Append("font-size:11px;font-weight:700;letter-spacing:0.22em;text-transform:uppercase;color:rgba(255,255,255,0.82);\">")
            .Append("Trader</p>")
            .Append("<h1 style=\"margin:0;font-family:system-ui,-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;")
            .Append("font-size:24px;font-weight:700;line-height:1.25;color:#ffffff;\">")
            .Append("Sign-in verification</h1>")
            .Append("<p style=\"margin:10px 0 0;font-family:system-ui,-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;")
            .Append("font-size:14px;line-height:1.5;color:rgba(255,255,255,0.9);\">")
            .Append("Enter the code below to finish signing in.</p>")
            .Append("</td></tr>");
    }

    private static void AppendBody(StringBuilder sb, HtmlEncoder enc, string code, string spacedCode, int expiryMinutes)
    {
        var expiry = enc.Encode(expiryMinutes.ToString(CultureInfo.InvariantCulture));
        var expiryLabel = expiryMinutes == 1 ? "minute" : "minutes";
        sb.Append("<tr><td style=\"padding:32px 32px 28px;font-family:system-ui,-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;")
            .Append("color:")
            .Append(Ink)
            .Append(";\">")
            .Append("<p style=\"margin:0 0 20px;font-size:15px;line-height:1.6;color:")
            .Append(Muted)
            .Append(";\">Use this one-time code on the sign-in screen. It works only once.</p>")

            .Append("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\"")
            .Append(" style=\"border-collapse:separate;border-spacing:0;margin:0 0 24px;\">")
            .Append("<tr><td align=\"center\" aria-label=\"Sign-in verification code\"")
            .Append(" style=\"-webkit-user-select:text;user-select:text;-webkit-touch-callout:default;")
            .Append("background:")
            .Append(Surface)
            .Append(";border:2px dashed #ced4da;border-radius:14px;padding:26px 20px 22px;\">")
            .Append("<p style=\"margin:0 0 10px;font-size:11px;font-weight:700;letter-spacing:0.16em;")
            .Append("text-transform:uppercase;color:")
            .Append(Muted)
            .Append(";\">Your code</p>")
            .Append("<p style=\"margin:0;font-size:38px;font-weight:800;line-height:1.1;letter-spacing:0.08em;")
            .Append("font-family:Consolas,'SF Mono',Menlo,Monaco,'Courier New',monospace;color:")
            .Append(Ink)
            .Append(";\">")
            .Append(spacedCode)
            .Append("</p>")
            .Append("<p style=\"margin:14px 0 0;font-size:13px;line-height:1.4;color:")
            .Append(Muted)
            .Append(";\">Plain digits: <strong style=\"font-family:Consolas,'SF Mono',Menlo,Monaco,'Courier New',monospace;")
            .Append("letter-spacing:0.12em;color:")
            .Append(Ink)
            .Append(";\">")
            .Append(code)
            .Append("</strong></p>")
            .Append("</td></tr></table>")

            .Append("<p style=\"margin:0 0 22px;text-align:center;\">")
            .Append("<span style=\"display:inline-block;padding:8px 16px;border-radius:999px;background:#fff3cd;")
            .Append("color:#664d03;font-size:13px;font-weight:600;line-height:1.4;\">")
            .Append("&#9201; Expires in ")
            .Append(expiry)
            .Append(' ')
            .Append(expiryLabel)
            .Append("</span></p>")

            .Append("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\"")
            .Append(" style=\"border-collapse:collapse;margin:0 0 8px;background:#f1f3f5;border-radius:10px;\">")
            .Append("<tr><td style=\"padding:14px 16px;border-left:4px solid ")
            .Append(BrandBlue)
            .Append(";\">")
            .Append("<p style=\"margin:0;font-size:13px;line-height:1.55;color:")
            .Append(Muted)
            .Append(";\"><strong style=\"color:")
            .Append(Ink)
            .Append(";\">Tip:</strong> Tap and hold the code, then choose ")
            .Append("<strong>Select All</strong> and <strong>Copy</strong> in your mail app.</p>")
            .Append("</td></tr></table>")

            .Append("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\"")
            .Append(" style=\"border-collapse:collapse;background:#fff5f5;border-radius:10px;border:1px solid #f1aeb5;\">")
            .Append("<tr><td style=\"padding:14px 16px;\">")
            .Append("<p style=\"margin:0;font-size:13px;line-height:1.55;color:#842029;\">")
            .Append("<strong>Didn&#39;t try to sign in?</strong> Change your password and contact support if this looks unexpected.")
            .Append("</p></td></tr></table>")
            .Append("</td></tr>");
    }

    private static void AppendFooter(StringBuilder sb)
    {
        sb.Append("<tr><td style=\"padding:18px 32px 22px;background:")
            .Append(Surface)
            .Append(";border-top:1px solid ")
            .Append(Border)
            .Append(";text-align:center;\">")
            .Append("<p style=\"margin:0;font-family:system-ui,-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;")
            .Append("font-size:12px;line-height:1.5;color:")
            .Append(Muted)
            .Append(";\">Automated message from Trader. Never share this code with anyone.</p>")
            .Append("</td></tr>");
    }

    private static string FormatSpacedOtp(string plainCode, HtmlEncoder enc)
    {
        if (plainCode.Length == 6 && int.TryParse(plainCode, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return enc.Encode(plainCode[..3]) + "<span style=\"display:inline-block;width:0.35em;\"></span>"
                + enc.Encode(plainCode[3..]);
        }

        return enc.Encode(plainCode);
    }
}
