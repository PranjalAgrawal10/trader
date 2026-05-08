using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Trader.Application.Exceptions;

namespace Trader.Api.Filters;

/// <summary>
/// Maps application/domain failures to ProblemDetails (SRP for controllers; DRY exception handling).
/// </summary>
public sealed class ApplicationExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.ExceptionHandled)
            return;

        ProblemDetails? problem = context.Exception switch
        {
            ConflictException ex => Problem(context, StatusCodes.Status409Conflict, "Conflict", ex.Message),
            InvalidOperationException ex => Problem(context, StatusCodes.Status400BadRequest, "Bad Request", ex.Message),
            _ => null,
        };

        if (problem is null)
            return;

        context.Result = problem.Status switch
        {
            StatusCodes.Status409Conflict => new ConflictObjectResult(problem),
            _ => new BadRequestObjectResult(problem),
        };
        context.ExceptionHandled = true;
    }

    private static ProblemDetails Problem(ExceptionContext context, int status, string title, string detail)
    {
        return new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context.HttpContext.Request.Path,
        };
    }
}
