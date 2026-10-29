using FluentValidation;
using Medical.Application.Common;

namespace Medical.Application.Users;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Doctor = "Doctor";
    public const string Nurse = "Nurse";
    public const string Billing = "Billing";

    public static readonly IReadOnlyList<string> All = [Admin, Doctor, Nurse, Billing];
}

public sealed record UserDto(string Id, string Email, string FullName, IReadOnlyList<string> Roles, bool IsActive);

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginResponse(string Token, DateTime ExpiresAt, UserDto User);

public sealed class CreateUserRequest
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
}

public sealed class UpdateUserRequest
{
    public string FullName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    public bool IsActive { get; set; }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithName("Correo electrónico");
        RuleFor(x => x.Password).NotEmpty().WithName("Contraseña");
    }
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256).WithName("Correo electrónico");
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200).WithName("Nombre completo");
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(100).WithName("Contraseña");
        RuleFor(x => x.Roles).ValidRoles();
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200).WithName("Nombre completo");
        RuleFor(x => x.Roles).ValidRoles();
    }
}

internal static class RoleRuleExtensions
{
    public static IRuleBuilderOptions<T, List<string>> ValidRoles<T>(this IRuleBuilder<T, List<string>> rule) =>
        rule.NotEmpty().WithMessage("Debe asignar al menos un rol.")
            .Must(roles => roles.All(r => Roles.All.Contains(r, StringComparer.OrdinalIgnoreCase)))
            .WithMessage($"Rol no válido. Roles permitidos: {string.Join(", ", Roles.All)}.");
}

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<UserDto> GetCurrentAsync(string userId, CancellationToken ct);
}

public interface IUserService
{
    Task<PagedResult<UserDto>> ListAsync(PageQuery query, CancellationToken ct);
    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct);
    Task<UserDto> UpdateAsync(string id, UpdateUserRequest request, string currentUserId, CancellationToken ct);
    Task DeleteAsync(string id, string currentUserId, CancellationToken ct);
}
