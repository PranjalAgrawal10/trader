namespace Trader.Application.Configuration;

/// <summary>URLs for links in outbound email (SPA base + optional path overrides).</summary>
public sealed class PublicWebOptions
{
    public const string SectionName = "PublicWeb";

    /// <summary>SPA origin, no trailing slash (e.g. <c>https://app.example.com</c> or dev <c>http://localhost:5173</c>).</summary>
    public string FrontendBaseUrl { get; set; } = "";

    public string VerifyEmailPath { get; set; } = "/verify-email";

    public string ResetPasswordPath { get; set; } = "/reset-password";
}
