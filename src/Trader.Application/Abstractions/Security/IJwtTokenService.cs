namespace Trader.Application.Abstractions.Security;

public interface IJwtTokenService
{
    string CreateToken(Guid userId, string email, string role);
}
