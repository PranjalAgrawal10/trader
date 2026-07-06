using Trader.Domain.Enums;

namespace Trader.Application.Auth;

internal sealed record EmailOtpTemplate(
    string Subject,
    string Preheader,
    string HeaderTitle,
    string HeaderSubtitle,
    string BodyIntro,
    string SecurityWarning,
    string PlainBodySuffix);

internal static class EmailOtpTemplates
{
    public static EmailOtpTemplate ForPurpose(EmailOtpPurpose purpose) =>
        purpose switch
        {
            EmailOtpPurpose.LoginSecondFactor => LoginSecondFactor,
            EmailOtpPurpose.PasswordReset => PasswordReset,
            _ => Generic,
        };

    public static string BuildPlainBody(EmailOtpTemplate template, string plainCode, int expiryMinutes) =>
        $"{template.PlainBodySuffix} {plainCode}. It expires in {expiryMinutes} minutes.";

    private static readonly EmailOtpTemplate LoginSecondFactor = new(
        Subject: "Your Trader sign-in code",
        Preheader: "Your Trader sign-in code",
        HeaderTitle: "Sign-in verification",
        HeaderSubtitle: "Enter the code below to finish signing in.",
        BodyIntro: "Use this one-time code on the sign-in screen. It works only once.",
        SecurityWarning:
            "<strong>Didn&#39;t try to sign in?</strong> Change your password and contact support if this looks unexpected.",
        PlainBodySuffix: "Your Trader sign-in code is:");

    private static readonly EmailOtpTemplate PasswordReset = new(
        Subject: "Your Trader password reset code",
        Preheader: "Your Trader password reset code",
        HeaderTitle: "Password reset",
        HeaderSubtitle: "Enter the code below on the forgot-password screen.",
        BodyIntro: "Use this one-time code to set a new password. It works only once.",
        SecurityWarning:
            "<strong>Didn&#39;t request a reset?</strong> Ignore this email. Your password stays unchanged.",
        PlainBodySuffix: "Your password reset code is:");

    private static readonly EmailOtpTemplate Generic = new(
        Subject: "Your verification code",
        Preheader: "Your verification code",
        HeaderTitle: "Verification code",
        HeaderSubtitle: "Enter the code where you requested it.",
        BodyIntro: "Use this one-time code to continue. It works only once.",
        SecurityWarning:
            "<strong>Didn&#39;t request this?</strong> You can ignore this email.",
        PlainBodySuffix: "Your verification code is:");
}
