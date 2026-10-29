namespace Medical.Application.Common;

/// <summary>Base type for expected business errors that map to a 4xx ProblemDetails response.</summary>
public abstract class AppException(string message) : Exception(message);

/// <summary>Maps to 404.</summary>
public sealed class NotFoundException(string message) : AppException(message);

/// <summary>Maps to 409.</summary>
public sealed class ConflictException(string message) : AppException(message);

/// <summary>Maps to 403.</summary>
public sealed class ForbiddenException(string message) : AppException(message);

/// <summary>Maps to 401.</summary>
public sealed class UnauthorizedException(string message) : AppException(message);

/// <summary>Maps to 400 with a field-keyed <c>errors</c> dictionary (camelCase keys).</summary>
public sealed class BusinessValidationException : AppException
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public BusinessValidationException(string field, string message)
        : base(message)
    {
        Errors = new Dictionary<string, string[]> { [field] = [message] };
    }

    public BusinessValidationException(IReadOnlyDictionary<string, string[]> errors, string message = "Se produjeron uno o más errores de validación.")
        : base(message)
    {
        Errors = errors;
    }
}
