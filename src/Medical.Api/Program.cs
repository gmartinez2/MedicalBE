using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Medical.Api.Infrastructure;
using Medical.Api.Security;
using Medical.Application;
using Medical.Application.Common;
using Medical.Infrastructure;
using Medical.Infrastructure.Identity;
using Medical.Infrastructure.Persistence;
using Medical.Infrastructure.Seed;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- Application + infrastructure ----------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(config);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<HttpCurrentUser>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<HttpCurrentUser>());

// ---------- MVC, JSON, validation, ProblemDetails ----------
builder.Services
    .AddControllers(options =>
    {
        options.Filters.Add<ValidationFilter>();
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
        ProblemDetailsSetup.UseSpanishModelBindingMessages(options.ModelBindingMessageProvider);
    })
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.AllowInputFormatterExceptionMessages = false; // keep System.Text.Json (English) internals out of responses
    })
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ProblemDetailsSetup.InvalidModelStateResponse);

builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ProblemDetailsSetup.Customize);
builder.Services.AddSwaggerDocumentation();

// ---------- Authentication (JWT) ----------
var jwtOptions = config.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var signingKey = JwtSigningKey.Resolve(jwtOptions.Key, builder.Environment.IsDevelopment());
builder.Services.AddSingleton(signingKey);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey.Key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "name",
            RoleClaimType = "role",
        };
        o.Events = new JwtBearerEvents
        {
            // Deactivated or deleted users lose access immediately, not when their token expires.
            OnTokenValidated = async ctx =>
            {
                var userId = ctx.Principal?.FindFirst("sub")?.Value;
                var db = ctx.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                var active = userId is not null &&
                             await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive, ctx.HttpContext.RequestAborted);
                if (!active) ctx.Fail("User is inactive or no longer exists.");
            },
        };
    });
builder.Services.AddAuthorizationBuilder().AddMedicalPolicies();

// ---------- CORS ----------
var allowedOrigins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:4200"];
builder.Services.AddCors(o => o.AddPolicy("Frontend", p => p
    .WithOrigins(allowedOrigins)
    .AllowAnyMethod()
    .WithHeaders("Authorization", "Content-Type", "Accept")
    .WithExposedHeaders("Content-Disposition")));

// ---------- Rate limiting (login brute-force protection) ----------
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(RateLimitPolicies.Login, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();

if (signingKey.IsEphemeral)
    app.Logger.LogWarning("Jwt:Key is not configured; using an EPHEMERAL signing key. Tokens become invalid on restart. Set it with 'dotnet user-secrets set Jwt:Key <base64>'.");

// ---------- Database: migrate (Development) + seed ----------
await using (var scope = app.Services.CreateAsyncScope())
{
    if (app.Environment.IsDevelopment())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
    }
}
await DatabaseSeeder.SeedAsync(app.Services, includeDemoData: app.Environment.IsDevelopment());

// ---------- Pipeline ----------
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/swagger/v1/swagger.json", "Medical System API v1");
        o.RoutePrefix = "swagger";
    });
}

app.UseRouting();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public static class RateLimitPolicies
{
    public const string Login = "login";
}

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program;
