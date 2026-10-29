using Medical.Api.Security;
using Medical.Application.Common;
using Medical.Application.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Medical.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(IAuthService auth, HttpCurrentUser currentUser) : ControllerBase
{
    /// <summary>Exchanges email + password for an 8-hour JWT.</summary>
    /// <response code="200">Authenticated.</response>
    /// <response code="401">Invalid credentials, locked out or inactive user.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public Task<LoginResponse> Login(LoginRequest request, CancellationToken ct) => auth.LoginAsync(request, ct);

    /// <summary>Returns the authenticated user.</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public Task<UserDto> Me(CancellationToken ct) => auth.GetCurrentAsync(currentUser.RequiredUserId, ct);
}

[ApiController]
[Route("api/users")]
[Authorize(Policy = Policies.AdminOnly)]
[Produces("application/json")]
public sealed class UsersController(IUserService users, HttpCurrentUser currentUser) : ControllerBase
{
    /// <summary>Lists users (search on email / full name).</summary>
    [HttpGet]
    public Task<PagedResult<UserDto>> List([FromQuery] PageQuery query, CancellationToken ct) => users.ListAsync(query, ct);

    /// <summary>Creates a user with the given roles.</summary>
    /// <response code="409">Email already registered.</response>
    [HttpPost]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await users.CreateAsync(request, ct));

    /// <summary>Updates name, roles and active flag.</summary>
    [HttpPut("{id}")]
    public Task<UserDto> Update(string id, UpdateUserRequest request, CancellationToken ct) =>
        users.UpdateAsync(id, request, currentUser.RequiredUserId, ct);

    /// <summary>Deletes a user (you cannot delete yourself).</summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await users.DeleteAsync(id, currentUser.RequiredUserId, ct);
        return NoContent();
    }
}
