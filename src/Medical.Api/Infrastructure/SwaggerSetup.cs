using System.Reflection;
using Microsoft.OpenApi;

namespace Medical.Api.Infrastructure;

public static class SwaggerSetup
{
    private const string SchemeId = "Bearer";

    public static IServiceCollection AddSwaggerDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Medical System API",
                Version = "v1",
                Description = "Hospital & clinic management backend. Contract: docs/API-CONTRACT.md.",
            });

            options.AddSecurityDefinition(SchemeId, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste the token returned by POST /api/auth/login.",
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(SchemeId, document)] = [],
            });

            var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
            if (File.Exists(xml)) options.IncludeXmlComments(xml);
            options.CustomSchemaIds(t => t.FullName?.Replace('+', '.'));
        });
        return services;
    }
}
