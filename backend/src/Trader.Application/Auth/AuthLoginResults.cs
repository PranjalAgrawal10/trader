namespace Trader.Application.Auth;

public abstract record LoginResult;

public sealed record LoginSucceeded(AuthResponse Auth) : LoginResult;

public sealed record LoginRequiresTwoFactor(string TwoFactorToken) : LoginResult;

public sealed record LoginRejected : LoginResult;
