using Microsoft.AspNetCore.Identity;

namespace Medical.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;

    /// <summary>Inactive users cannot log in and their existing tokens are rejected.</summary>
    public bool IsActive { get; set; } = true;
}
