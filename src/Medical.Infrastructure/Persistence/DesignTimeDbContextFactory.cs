using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Medical.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> so migrations never boot the web host (and never need the JWT key).
/// Override the connection with the <c>MEDICAL_DB_CONNECTION</c> environment variable.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public const string DefaultConnection =
        @"Server=localhost\SQLEXPRESS;Database=MedicalSystemDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("MEDICAL_DB_CONNECTION") ?? DefaultConnection;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection, sql => sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .Options;
        return new ApplicationDbContext(options);
    }
}
