using Medical.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Medical.Api.Infrastructure;

/// <summary>
/// Outermost middleware: translates exceptions into RFC 7807 ProblemDetails with Spanish messages.
/// Unexpected errors are logged with full detail but never leak internals to the client.
/// </summary>
public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IProblemDetailsService problemDetails)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client went away; nothing useful to send.
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                logger.LogError(ex, "Unhandled exception after the response started on {Method} {Path}", context.Request.Method, context.Request.Path);
                throw;
            }

            var problem = Map(ex);
            if (problem.Status >= 500)
                logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
            else
                logger.LogInformation("Request {Method} {Path} failed with {Status}: {Message}", context.Request.Method, context.Request.Path, problem.Status, ex.Message);

            context.Response.Clear();
            context.Response.StatusCode = problem.Status!.Value;
            await problemDetails.WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem, Exception = ex });
        }
    }

    private static ProblemDetails Map(Exception ex) => ex switch
    {
        BusinessValidationException v => Validation(v.Errors.ToDictionary(kv => kv.Key, kv => kv.Value)),
        FluentValidation.ValidationException fv => Validation(fv.Errors
            .GroupBy(e => ProblemDetailsSetup.ToFieldKey(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())),
        NotFoundException => Problem(404, ex.Message),
        ConflictException => Problem(409, ex.Message),
        ForbiddenException => Problem(403, ex.Message),
        UnauthorizedException => Problem(401, ex.Message),
        DbUpdateConcurrencyException => Problem(409, "El registro fue modificado por otro usuario. Recargue los datos e intente nuevamente."),
        DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } =>
            Problem(409, "Ya existe un registro con los mismos datos únicos."),
        DbUpdateException { InnerException: SqlException { Number: 547 } } =>
            Problem(409, "La operación viola una restricción de integridad de los datos."),
        BadHttpRequestException { StatusCode: 413 } => Problem(413, "La solicitud excede el tamaño máximo permitido."),
        BadHttpRequestException b => Problem(b.StatusCode, "La solicitud no es válida."),
        InvalidDataException => Problem(400, "El contenido enviado no es válido o excede el tamaño permitido."),
        _ => Problem(500, "Ocurrió un error inesperado. Intente nuevamente más tarde."),
    };

    private static ProblemDetails Problem(int status, string detail) => new()
    {
        Status = status,
        Title = ProblemDetailsSetup.TitleFor(status),
        Detail = detail,
    };

    // IProblemDetailsService serializes the declared ProblemDetails type, so a ValidationProblemDetails
    // would lose its "errors" member; carry them as an extension instead (same JSON shape).
    private static ProblemDetails Validation(IDictionary<string, string[]> errors)
    {
        var problem = new ProblemDetails
        {
            Status = 400,
            Title = ProblemDetailsSetup.ValidationTitle,
            Detail = string.Join(" ", errors.Values.SelectMany(v => v).Distinct()),
        };
        problem.Extensions["errors"] = errors;
        return problem;
    }
}

/// <summary>Security headers for a JSON API; Swagger UI gets a relaxed CSP so it can render.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers.XXSSProtection = "0";
        headers.ContentSecurityPolicy = context.Request.Path.StartsWithSegments("/swagger")
            ? "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'"
            : "default-src 'none'; frame-ancestors 'none'";
        return next(context);
    }
}
