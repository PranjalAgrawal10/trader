namespace Trader.Domain.Enums;

/// <summary>How the user completes the password + second step at sign-in.</summary>
public enum SecondFactorMethod : byte
{
    None = 0,
    AuthenticatorApp = 1,
    EmailSignInCode = 2,
}
