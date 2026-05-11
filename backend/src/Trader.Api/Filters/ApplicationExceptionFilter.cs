using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Trader.Application.Exceptions;

namespace Trader.Api.Filters;

/// <summary>
/// Maps application/domain failures to ProblemDetails (SRP for controllers; DRY exception handling).
/// </summary>
public sealed class ApplicationExceptionFilter : IExceptionFilter
{
    private readonly ILogger<ApplicationExceptionFilter> _logger;

    public ApplicationExceptionFilter(ILogger<ApplicationExceptionFilter> logger)
    {
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        if (context.ExceptionHandled)
            return;

        // These are mapped to 4xx responses; log without stack trace to avoid noisy handled-exception logs.
        // Unmapped exceptions will still flow to the host and be logged as 5xx by Serilog request logging.
        ProblemDetails? problem = context.Exception switch
        {
            ConflictException ex => LogAndMap(context, StatusCodes.Status409Conflict, "Conflict", ex.Message),
            InvalidOperationException ex => LogAndMap(context, StatusCodes.Status400BadRequest, "Bad Request", ex.Message),
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

    private ProblemDetails LogAndMap(ExceptionContext context, int status, string title, string detail)
    {
        _logger.LogWarning(
            "Handled exception mapped to {StatusCode} for {Method} {Path}: {Title} - {Detail}",
            status,
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path.Value,
            title,
            detail);

        return Problem(context, status, title, detail);
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
