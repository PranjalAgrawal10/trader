using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace Trader.Api.Routing;

[ApiController]
[ApiVersion(ApiRoutes.Version)]
[ApiExplorerSettings(GroupName = ApiRoutes.SwaggerGroup)]
public abstract class V1ControllerBase : ControllerBase;

/// <summary>Resolves to <c>api/v1/{controller}</c> (controller name without <c>Controller</c> suffix).</summary>
[Route(ApiRoutes.VersionedPrefix + "/[controller]")]
public abstract class V1NamedControllerBase : V1ControllerBase;

/// <summary>Fixed segment under the versioned API prefix, e.g. <c>broker</c>, <c>2fa</c>.</summary>
public sealed class V1RouteAttribute : RouteAttribute
{
    public V1RouteAttribute(string segment)
        : base(ApiRoutes.V1(segment))
    {
    }
}
