using Medical.Application.Common;
using Medical.Application.Users;
using Medical.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Medical.Infrastructure.Identity;

public sealed class AuthService(UserManager<ApplicationUser> users, IJwtTokenService tokens) : IAuthService
{
    private const string InvalidCredentials = "Credenciales inválidas.";

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim())
                   ?? throw new UnauthorizedException(InvalidCredentials);

        if (await users.IsLockedOutAsync(user))
            throw new UnauthorizedException("La cuenta está bloqueada temporalmente por intentos fallidos. Intente más tarde.");

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            throw new UnauthorizedException(InvalidCredentials);
        }

        await users.ResetAccessFailedCountAsync(user);

        // Checked after the password so the endpoint does not reveal which accounts exist.
        if (!user.IsActive) throw new UnauthorizedException("Usuario inactivo.");

        var roles = await users.GetRolesAsync(user);
        var (token, expiresAt) = tokens.CreateToken(user, roles);
        return new LoginResponse(token, expiresAt, ToDto(user, roles));
    }

    public async Task<UserDto> GetCurrentAsync(string userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId) ?? throw new NotFoundException("Usuario no encontrado.");
        return ToDto(user, await users.GetRolesAsync(user));
    }

    internal static UserDto ToDto(ApplicationUser user, IEnumerable<string> roles) =>
        new(user.Id, user.Email ?? string.Empty, user.FullName, roles.Order().ToList(), user.IsActive);
}

public sealed class UserService(UserManager<ApplicationUser> users, ApplicationDbContext db) : IUserService
{
    private const string NotFoundMessage = "Usuario no encontrado.";

    public async Task<PagedResult<UserDto>> ListAsync(PageQuery query, CancellationToken ct)
    {
        var q = db.Users.AsNoTracking();
        if (query.NormalizedSearch is { } s)
            q = q.Where(u => u.Email!.Contains(s) || u.FullName.Contains(s));

        var page = await q.OrderBy(u => u.FullName).ThenBy(u => u.Email)
            .Select(u => new { u.Id, u.Email, u.FullName, u.IsActive })
            .ToPagedAsync(query, ct);

        // One query for the roles of the whole page (no N+1).
        var ids = page.Items.Select(u => u.Id).ToList();
        var roles = await (from ur in db.UserRoles
                           join r in db.Roles on ur.RoleId equals r.Id
                           where ids.Contains(ur.UserId)
                           select new { ur.UserId, r.Name })
            .AsNoTracking()
            .ToListAsync(ct);
        var rolesBy = roles.ToLookup(x => x.UserId, x => x.Name!);

        var items = page.Items
            .Select(u => new UserDto(u.Id, u.Email ?? string.Empty, u.FullName, rolesBy[u.Id].Order().ToList(), u.IsActive))
            .ToList();
        return new PagedResult<UserDto>(items, page.TotalCount, page.Page, page.PageSize);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim();
        if (await users.FindByEmailAsync(email) is not null)
            throw new ConflictException("Ya existe un usuario con ese correo electrónico.");

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = request.FullName.Trim(),
            IsActive = true,
        };
        ThrowIfFailed(await users.CreateAsync(user, request.Password));
        var roles = Canonical(request.Roles);
        ThrowIfFailed(await users.AddToRolesAsync(user, roles));
        return AuthService.ToDto(user, roles);
    }

    public async Task<UserDto> UpdateAsync(string id, UpdateUserRequest request, string currentUserId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id) ?? throw new NotFoundException(NotFoundMessage);
        var roles = Canonical(request.Roles);

        if (id == currentUserId && (!request.IsActive || !roles.Contains(Roles.Admin)))
            throw new BusinessValidationException("isActive", "No puede desactivar su propia cuenta ni quitarse el rol de administrador.");

        user.FullName = request.FullName.Trim();
        user.IsActive = request.IsActive;
        ThrowIfFailed(await users.UpdateAsync(user));

        var current = await users.GetRolesAsync(user);
        var toRemove = current.Except(roles).ToList();
        var toAdd = roles.Except(current).ToList();
        if (toRemove.Count > 0) ThrowIfFailed(await users.RemoveFromRolesAsync(user, toRemove));
        if (toAdd.Count > 0) ThrowIfFailed(await users.AddToRolesAsync(user, toAdd));

        return AuthService.ToDto(user, roles);
    }

    public async Task DeleteAsync(string id, string currentUserId, CancellationToken ct)
    {
        if (id == currentUserId)
            throw new BusinessValidationException("id", "No puede eliminar su propia cuenta.");
        var user = await users.FindByIdAsync(id) ?? throw new NotFoundException(NotFoundMessage);
        ThrowIfFailed(await users.DeleteAsync(user));
    }

    private static List<string> Canonical(IEnumerable<string> roles) =>
        roles.Select(r => Roles.All.First(x => string.Equals(x, r, StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .ToList();

    private static void ThrowIfFailed(IdentityResult result)
    {
        if (result.Succeeded) return;
        if (result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
            throw new ConflictException("Ya existe un usuario con ese correo electrónico.");

        var errors = result.Errors
            .GroupBy(e => e.Code.StartsWith("Password", StringComparison.Ordinal) ? "password"
                : e.Code.Contains("Email", StringComparison.Ordinal) || e.Code.Contains("UserName", StringComparison.Ordinal) ? "email"
                : "roles")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
        throw new BusinessValidationException(errors);
    }
}
