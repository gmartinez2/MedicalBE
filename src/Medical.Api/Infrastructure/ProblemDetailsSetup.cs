using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace Medical.Api.Infrastructure;

/// <summary>Spanish ProblemDetails titles and helpers shared by the exception middleware and validation.</summary>
public static class ProblemDetailsSetup
{
    public const string ValidationTitle = "Se produjeron uno o más errores de validación.";

    private static readonly Dictionary<int, string> SpanishTitles = new()
    {
        [400] = "Solicitud inválida",
        [401] = "No autenticado",
        [403] = "Acceso denegado",
        [404] = "Recurso no encontrado",
        [405] = "Método no permitido",
        [409] = "Conflicto",
        [413] = "Solicitud demasiado grande",
        [415] = "Tipo de contenido no soportado",
        [429] = "Demasiadas solicitudes",
        [500] = "Error interno del servidor",
    };

    private static readonly Dictionary<int, string> DefaultDetails = new()
    {
        [401] = "Debe iniciar sesión para acceder a este recurso.",
        [403] = "No tiene permisos para realizar esta acción.",
        [404] = "El recurso solicitado no existe.",
        [405] = "El método HTTP no está permitido para este recurso.",
        [415] = "El tipo de contenido de la solicitud no es compatible.",
        [429] = "Se excedió el número de solicitudes permitidas. Intente nuevamente en unos minutos.",
    };

    public static string TitleFor(int status) => SpanishTitles.GetValueOrDefault(status, "Error");

    /// <summary>Replaces framework default (English) titles with Spanish and fills a default detail.</summary>
    public static void Customize(ProblemDetailsContext context)
    {
        var pd = context.ProblemDetails;
        var status = pd.Status ?? context.HttpContext.Response.StatusCode;
        if (pd is ValidationProblemDetails && (pd.Title is null || pd.Title.StartsWith("One or more", StringComparison.Ordinal)))
            pd.Title = ValidationTitle;
        else if (pd.Title is null || IsFrameworkDefaultTitle(pd.Title))
            pd.Title = TitleFor(status);
        pd.Detail ??= DefaultDetails.GetValueOrDefault(status);
        pd.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    }

    private static bool IsFrameworkDefaultTitle(string title) => title is
        "Bad Request" or "Unauthorized" or "Forbidden" or "Not Found" or "Method Not Allowed" or "Not Acceptable"
        or "Request Timeout" or "Conflict" or "Payload Too Large" or "Unsupported Media Type" or "Unprocessable Entity"
        or "Too Many Requests" or "Internal Server Error" or "An error occurred while processing your request.";

    /// <summary>camelCases each segment of a property path: <c>Roles[0]</c> → <c>roles[0]</c>, <c>$.dateOfBirth</c> → <c>dateOfBirth</c>.</summary>
    public static string ToFieldKey(string propertyPath)
    {
        var path = propertyPath.StartsWith("$.", StringComparison.Ordinal) ? propertyPath[2..] : propertyPath;
        if (path.Length == 0 || path == "$") return "body";
        return string.Join('.', path.Split('.').Select(s => s.Length == 0 ? s : JsonNamingPolicy.CamelCase.ConvertName(s)));
    }

    /// <summary>Spanish model-binding messages (missing body, malformed values, etc.).</summary>
    public static void UseSpanishModelBindingMessages(DefaultModelBindingMessageProvider p)
    {
        p.SetMissingBindRequiredValueAccessor(name => $"Falta un valor para el campo '{name}'.");
        p.SetMissingKeyOrValueAccessor(() => "Se requiere un valor.");
        p.SetMissingRequestBodyRequiredValueAccessor(() => "El cuerpo de la solicitud no puede estar vacío.");
        p.SetValueMustNotBeNullAccessor(value => $"El valor '{value}' no es válido.");
        p.SetAttemptedValueIsInvalidAccessor((value, name) => $"El valor '{value}' no es válido para '{name}'.");
        p.SetNonPropertyAttemptedValueIsInvalidAccessor(value => $"El valor '{value}' no es válido.");
        p.SetUnknownValueIsInvalidAccessor(name => $"El valor proporcionado no es válido para '{name}'.");
        p.SetNonPropertyUnknownValueIsInvalidAccessor(() => "El valor proporcionado no es válido.");
        p.SetValueIsInvalidAccessor(value => $"El valor '{value}' no es válido.");
        p.SetValueMustBeANumberAccessor(name => $"El campo '{name}' debe ser un número.");
        p.SetNonPropertyValueMustBeANumberAccessor(() => "El campo debe ser un número.");
    }

    /// <summary>Builds the 400 body for invalid model state with camelCase field keys and Spanish messages.</summary>
    public static IActionResult InvalidModelStateResponse(ActionContext context)
    {
        var errors = context.ModelState
            .Where(kv => kv.Value is { Errors.Count: > 0 })
            .GroupBy(kv => ToFieldKey(kv.Key))
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(kv => kv.Value!.Errors)
                    .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "El valor proporcionado no es válido." : e.ErrorMessage)
                    .Distinct()
                    .ToArray());

        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = ValidationTitle,
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            Detail = string.Join(" ", errors.Values.SelectMany(v => v).Distinct()),
        };
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    }
}
