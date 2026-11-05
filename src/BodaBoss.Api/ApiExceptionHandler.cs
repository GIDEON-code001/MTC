using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BodaBoss.Api;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title, detail) = exception switch
        {
            ValidationException validation => (
                StatusCodes.Status400BadRequest,
                "Validation failed",
                string.Join("; ", validation.Errors.Select(error => $"{error.PropertyName}: {error.ErrorMessage}"))),
            KeyNotFoundException notFound => (
                StatusCodes.Status404NotFound,
                "Resource not found",
                notFound.Message),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                (StatusCodes.Status409Conflict, "Conflict", "A record with the same unique value already exists."),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected server error",
                "The request could not be completed.")
        };

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception processing {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation("Request rejected with {StatusCode}: {ExceptionType}",
                statusCode, exception.GetType().Name);
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        }, cancellationToken);
        return true;
    }
}
