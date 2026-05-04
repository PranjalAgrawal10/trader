namespace Trader.Application.Auth;

public sealed record RegisterRequest(string Email, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record AuthResponse(string Token, Guid UserId, string Email, string Role);
