using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace Trader.Api.Extensions;

public static class ClaimsExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var raw =
            user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub");

        if (raw is null || !Guid.TryParse(raw, out var id))
            throw new UnauthorizedAccessException("Missing or invalid user id claim.");

        return id;
    }
}
