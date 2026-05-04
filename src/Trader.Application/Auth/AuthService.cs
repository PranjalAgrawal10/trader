using Trader.Application.Abstractions.Persistence;
using Trader.Application.Abstractions.Security;
using Trader.Application.Exceptions;
using Trader.Domain.Entities;
using Trader.Domain.Enums;

namespace Trader.Application.Auth;

public sealed class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;

    public AuthService(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (await _users.ExistsByEmailAsync(normalizedEmail, ct))
            throw new ConflictException("Email is already registered.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            PasswordHash = _passwordHasher.Hash(request.Password),
            Role = UserRole.Trader,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _users.AddAsync(user, ct);
        await _users.SaveChangesAsync(ct);

        var token = _jwtTokenService.CreateToken(user.Id, user.Email, user.Role);
        return new AuthResponse(token, user.Id, user.Email, user.Role);
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _users.GetByEmailAsync(normalizedEmail, ct);
        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
            return null;

        var token = _jwtTokenService.CreateToken(user.Id, user.Email, user.Role);
        return new AuthResponse(token, user.Id, user.Email, user.Role);
    }
}
