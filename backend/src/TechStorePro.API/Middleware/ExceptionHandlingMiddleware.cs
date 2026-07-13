using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Inventory;
using Microsoft.AspNetCore.Mvc;

namespace TechStorePro.API.Middleware;

/// <summary>
/// Translates known exception types into RFC 7807 problem details so that every error
/// response in the API has the same shape. Unknown exceptions are logged and reported as
/// a generic 500 — internal details never reach the client.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        ProblemDetails problem;

        switch (exception)
        {
            case ValidationException validation:
                problem = new ValidationProblemDetails(validation.Errors)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "One or more validation errors occurred."
                };
                break;

            // Before DomainException, which it derives from — the switch takes the first match, and a
            // "not enough stock" reported as 400 would tell the client to fix its request when there is
            // nothing wrong with it. The request was valid; the shelf disagrees (api-design.md §4).
            case InsufficientStockException stock:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status422UnprocessableEntity,
                    Title = "Not enough stock.",
                    Detail = stock.Message
                };
                problem.Extensions["productId"] = stock.ProductId;
                problem.Extensions["warehouseId"] = stock.WarehouseId;
                problem.Extensions["requested"] = stock.Requested;
                problem.Extensions["available"] = stock.Available;
                break;

            case DomainException domain:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "The request violates a business rule.",
                    Detail = domain.Message
                };
                break;

            case NotFoundException notFound:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Resource not found.",
                    Detail = notFound.Message
                };
                break;

            case ForbiddenException forbidden:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Permission denied.",
                    Detail = forbidden.Message
                };
                break;

            case ConflictException conflict:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "The request conflicts with the current state.",
                    Detail = conflict.Message
                };
                break;

            // Failed authentication, not failed authorisation: the caller is not (or is no longer)
            // who they need to be, so 401 tells them to sign in again. A 403 here would send the
            // client into a loop, retrying a request that no fresh token could ever fix.
            case UnauthorizedAccessException unauthorised:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status401Unauthorized,
                    Title = "Authentication failed.",
                    Detail = unauthorised.Message
                };
                break;

            default:
                _logger.LogError(exception, "Unhandled exception while processing {Path}", context.Request.Path);
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "An unexpected error occurred."
                };
                break;
        }

        problem.Instance = context.Request.Path;
        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    }
}
