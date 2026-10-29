using System.Linq.Expressions;
using FluentValidation;
using Medical.Application.Common;
using Medical.Application.Patients;
using Medical.Domain;
using Medical.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Medical.Application.Clinical;

public class VitalSignInput
{
    public DateTime RecordedAt { get; set; }
    public int Systolic { get; set; }
    public int Diastolic { get; set; }
    public decimal Glucose { get; set; }
    public int OxygenSaturation { get; set; }
    public int? HeartRate { get; set; }
    public decimal? Temperature { get; set; }
    public string? Notes { get; set; }
}

public class VitalSignDto : VitalSignInput
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public string? RecordedBy { get; set; }

    /// <summary>Clinical flags derived from the thresholds in <see cref="VitalSignRules"/>.</summary>
    public IReadOnlyList<string> Flags =>
        VitalSignRules.ComputeFlags(Systolic, Diastolic, Glucose, OxygenSaturation, HeartRate, Temperature);

    public static readonly Expression<Func<VitalSign, VitalSignDto>> Projection = v => new VitalSignDto
    {
        Id = v.Id,
        PatientId = v.PatientId,
        RecordedAt = v.RecordedAt,
        Systolic = v.Systolic,
        Diastolic = v.Diastolic,
        Glucose = v.Glucose,
        OxygenSaturation = v.OxygenSaturation,
        HeartRate = v.HeartRate,
        Temperature = v.Temperature,
        Notes = v.Notes,
        RecordedBy = v.CreatedBy,
    };
}

/// <summary>Vital sign enriched with the patient name (dashboard alerts).</summary>
public sealed class VitalSignAlertDto : VitalSignDto
{
    public string PatientName { get; set; } = string.Empty;
}

public sealed class VitalSignInputValidator : AbstractValidator<VitalSignInput>
{
    public VitalSignInputValidator(TimeProvider clock)
    {
        RuleFor(x => x.RecordedAt).NotEmpty().WithName("Fecha de registro")
            .Must(d => DateTimeUtc.Normalize(d) <= clock.GetUtcNow().UtcDateTime.AddMinutes(5))
            .WithMessage("La fecha de registro no puede estar en el futuro.");
        RuleFor(x => x.Systolic).InclusiveBetween(50, 260).WithName("Presión sistólica");
        RuleFor(x => x.Diastolic).InclusiveBetween(30, 160).WithName("Presión diastólica");
        RuleFor(x => x.Diastolic).LessThan(x => x.Systolic)
            .WithMessage("La presión diastólica debe ser menor que la sistólica.");
        RuleFor(x => x.Glucose).InclusiveBetween(20m, 600m).PrecisionScale(5, 1, true).WithName("Glucosa");
        RuleFor(x => x.OxygenSaturation).InclusiveBetween(50, 100).WithName("Saturación de oxígeno");
        RuleFor(x => x.HeartRate).InclusiveBetween(20, 250).When(x => x.HeartRate is not null).WithName("Frecuencia cardíaca");
        RuleFor(x => x.Temperature).InclusiveBetween(30m, 45m).PrecisionScale(5, 1, true)
            .When(x => x.Temperature is not null).WithName("Temperatura");
        RuleFor(x => x.Notes).MaximumLength(1000).WithName("Notas");
    }
}

public interface IVitalSignService
{
    Task<IReadOnlyList<VitalSignDto>> ListByPatientAsync(int patientId, DateTime? from, DateTime? to, CancellationToken ct);
    Task<VitalSignDto> CreateAsync(int patientId, VitalSignInput input, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}

public sealed class VitalSignService(IAppDbContext db) : IVitalSignService
{
    /// <summary>SQL-translatable predicate equivalent to "ComputeFlags returns at least one flag".</summary>
    public static readonly Expression<Func<VitalSign, bool>> IsAbnormal = v =>
        v.Systolic >= VitalSignRules.HighSystolic || v.Diastolic >= VitalSignRules.HighDiastolic ||
        v.Systolic < VitalSignRules.LowSystolic || v.Diastolic < VitalSignRules.LowDiastolic ||
        v.Glucose > VitalSignRules.HighGlucose || v.Glucose < VitalSignRules.LowGlucose ||
        v.OxygenSaturation < VitalSignRules.LowOxygen ||
        v.HeartRate > VitalSignRules.TachycardiaHeartRate ||
        v.Temperature >= VitalSignRules.FeverTemperature;

    public async Task<IReadOnlyList<VitalSignDto>> ListByPatientAsync(int patientId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        await PatientService.EnsureExistsAsync(db, patientId, ct);
        var vitals = db.VitalSigns.AsNoTracking().Where(v => v.PatientId == patientId);
        if (DateTimeUtc.Normalize(from) is { } f) vitals = vitals.Where(v => v.RecordedAt >= f);
        if (DateTimeUtc.Normalize(to) is { } t) vitals = vitals.Where(v => v.RecordedAt <= t);
        return await vitals.OrderByDescending(v => v.RecordedAt).ThenByDescending(v => v.Id)
            .Select(VitalSignDto.Projection).ToListAsync(ct);
    }

    public async Task<VitalSignDto> CreateAsync(int patientId, VitalSignInput input, CancellationToken ct)
    {
        await PatientService.EnsureExistsAsync(db, patientId, ct);
        var vital = new VitalSign
        {
            PatientId = patientId,
            RecordedAt = DateTimeUtc.Normalize(input.RecordedAt),
            Systolic = input.Systolic,
            Diastolic = input.Diastolic,
            Glucose = input.Glucose,
            OxygenSaturation = input.OxygenSaturation,
            HeartRate = input.HeartRate,
            Temperature = input.Temperature,
            Notes = input.Notes.NullIfBlank(),
        };
        db.VitalSigns.Add(vital);
        await db.SaveChangesAsync(ct);
        return await db.VitalSigns.AsNoTracking().Where(v => v.Id == vital.Id).Select(VitalSignDto.Projection).FirstAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        if (await db.VitalSigns.Where(v => v.Id == id).ExecuteDeleteAsync(ct) == 0)
            throw new NotFoundException("Registro de signos vitales no encontrado.");
    }
}
