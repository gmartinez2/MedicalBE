using Medical.Application.Common;

namespace Medical.Api.Security;

/// <summary>Reads the caller identity from the validated JWT (claims are not remapped: sub, name, email, role).</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? UserId => accessor.HttpContext?.User.FindFirst("sub")?.Value;

    public string DisplayName
    {
        get
        {
            var user = accessor.HttpContext?.User;
            var name = user?.FindFirst("name")?.Value;
            if (!string.IsNullOrWhiteSpace(name)) return name;
            var email = user?.FindFirst("email")?.Value;
            return string.IsNullOrWhiteSpace(email) ? "System" : email;
        }
    }

    /// <summary>User id for endpoints that require authentication (never null there).</summary>
    public string RequiredUserId => UserId ?? throw new UnauthorizedException("Debe iniciar sesión para acceder a este recurso.");
}
