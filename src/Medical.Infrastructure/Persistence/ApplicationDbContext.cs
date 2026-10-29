using Medical.Application.Common;
using Medical.Domain.Common;
using Medical.Domain.Entities;
using Medical.Domain.Enums;
using Medical.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Medical.Infrastructure.Persistence;

public class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    ICurrentUser? currentUser = null,
    TimeProvider? clock = null)
    : IdentityDbContext<ApplicationUser>(options), IAppDbContext
{
    private const string SystemUser = "System";

    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<MedicalRecord> MedicalRecords => Set<MedicalRecord>();
    public DbSet<Diagnosis> Diagnoses => Set<Diagnosis>();
    public DbSet<Treatment> Treatments => Set<Treatment>();
    public DbSet<MedicalImage> MedicalImages => Set<MedicalImage>();
    public DbSet<VitalSign> VitalSigns => Set<VitalSign>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<Medication> Medications => Set<Medication>();
    public DbSet<PaymentPlan> PaymentPlans => Set<PaymentPlan>();
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        foreach (var entity in builder.Model.GetEntityTypes().Where(t => typeof(AuditableEntity).IsAssignableFrom(t.ClrType)))
        {
            builder.Entity(entity.ClrType).Property(nameof(AuditableEntity.CreatedBy)).HasMaxLength(256);
            builder.Entity(entity.ClrType).Property(nameof(AuditableEntity.UpdatedBy)).HasMaxLength(256);
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // SQL Server datetime2 drops DateTimeKind; every DateTime in this model is UTC.
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        builder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();

        // Enums stored as readable strings, matching the API contract values.
        builder.Properties<Gender>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<DiagnosisStatus>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<TreatmentStatus>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<PaymentFrequency>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<PaymentPlanStatus>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<PaymentMethod>().HaveConversion<string>().HaveMaxLength(20);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAudit();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAudit();
        return base.SaveChanges();
    }

    private void ApplyAudit()
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var user = currentUser?.DisplayName ?? SystemUser;
        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy ??= user;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = user;
                    break;
            }
        }
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v == null ? v : v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc),
        v => v == null ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));
}
