using Medical.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Medical.Application.Common;

/// <summary>Application view of the EF Core context. Implemented in Infrastructure.</summary>
public interface IAppDbContext
{
    DbSet<Patient> Patients { get; }
    DbSet<Doctor> Doctors { get; }
    DbSet<MedicalRecord> MedicalRecords { get; }
    DbSet<Diagnosis> Diagnoses { get; }
    DbSet<Treatment> Treatments { get; }
    DbSet<MedicalImage> MedicalImages { get; }
    DbSet<VitalSign> VitalSigns { get; }
    DbSet<Shift> Shifts { get; }
    DbSet<Medication> Medications { get; }
    DbSet<PaymentPlan> PaymentPlans { get; }
    DbSet<Payment> Payments { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Identity of the caller for the current request.</summary>
public interface ICurrentUser
{
    string? UserId { get; }

    /// <summary>Human-readable name used for audit columns (full name, falling back to email).</summary>
    string DisplayName { get; }
}

/// <summary>Binary storage for uploaded medical images.</summary>
public interface IFileStorage
{
    /// <summary>Persists the stream and returns the storage-relative path (GUID + extension).</summary>
    Task<string> SaveAsync(Stream content, string extension, CancellationToken ct);

    /// <summary>Opens a stored file for reading, or returns null when it no longer exists.</summary>
    Stream? OpenRead(string relativePath);

    void Delete(string relativePath);
}

public static class DateTimeUtc
{
    /// <summary>Normalizes an incoming DateTime to UTC. Unspecified values are assumed to already be UTC.</summary>
    public static DateTime Normalize(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    public static DateTime? Normalize(DateTime? value) => value is null ? null : Normalize(value.Value);
}
