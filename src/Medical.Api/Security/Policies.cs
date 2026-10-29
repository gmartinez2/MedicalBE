using Medical.Application.Users;
using Microsoft.AspNetCore.Authorization;

namespace Medical.Api.Security;

/// <summary>Authorization policies mirroring the role table in docs/API-CONTRACT.md.</summary>
public static class Policies
{
    /// <summary>Admin, Doctor, Nurse, Billing.</summary>
    public const string AnyStaff = nameof(AnyStaff);

    /// <summary>Admin, Doctor, Nurse.</summary>
    public const string ClinicalStaff = nameof(ClinicalStaff);

    /// <summary>Admin, Doctor.</summary>
    public const string Physicians = nameof(Physicians);

    /// <summary>Admin, Billing.</summary>
    public const string BillingStaff = nameof(BillingStaff);

    /// <summary>Admin.</summary>
    public const string AdminOnly = nameof(AdminOnly);

    public static AuthorizationBuilder AddMedicalPolicies(this AuthorizationBuilder builder) => builder
        // Any endpoint without an explicit policy still requires a valid token.
        .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
        .AddPolicy(AnyStaff, p => p.RequireRole(Roles.Admin, Roles.Doctor, Roles.Nurse, Roles.Billing))
        .AddPolicy(ClinicalStaff, p => p.RequireRole(Roles.Admin, Roles.Doctor, Roles.Nurse))
        .AddPolicy(Physicians, p => p.RequireRole(Roles.Admin, Roles.Doctor))
        .AddPolicy(BillingStaff, p => p.RequireRole(Roles.Admin, Roles.Billing))
        .AddPolicy(AdminOnly, p => p.RequireRole(Roles.Admin));
}
