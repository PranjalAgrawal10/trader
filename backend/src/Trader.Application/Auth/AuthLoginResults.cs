namespace Trader.Application.Auth;
public abstract record LoginResult;

public sealed record LoginSucceeded(AuthResponse Auth) : LoginResult;

/// <param name="SecondFactorKind"><c>authenticator</c> or <c>email_otp</c> for the client UX.</param>
public sealed record LoginRequiresTwoFactor(string TwoFactorToken, string SecondFactorKind) : LoginResult;

/// <summary>Password was correct but registration email must be confirmed before sign-in.</summary>
public sealed record LoginRequiresEmailVerification : LoginResult;

public sealed record LoginRejected : LoginResult;

public abstract record RegistrationResult;

/// <summary>Account created and a verification link was mailed; no JWT is issued.</summary>
public sealed record RegistrationPendingEmailVerification(string EmailNormalized) : RegistrationResult;
