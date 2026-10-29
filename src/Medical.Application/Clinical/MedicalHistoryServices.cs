using System.Linq.Expressions;
using FluentValidation;
using Medical.Application.Common;
using Medical.Application.Doctors;
using Medical.Application.Patients;
using Medical.Domain.Entities;
using Medical.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Medical.Application.Clinical;

// ---------- Medical records ----------

public class MedicalRecordInput
{
    public DateTime VisitDate { get; set; }
    public int? DoctorId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class MedicalRecordDto : MedicalRecordInput
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public string? DoctorName { get; set; }

    public static readonly Expression<Func<MedicalRecord, MedicalRecordDto>> Projection = r => new MedicalRecordDto
    {
        Id = r.Id,
        PatientId = r.PatientId,
        VisitDate = r.VisitDate,
        DoctorId = r.DoctorId,
        DoctorName = r.Doctor == null ? null : r.Doctor.FirstName + " " + r.Doctor.LastName,
        Reason = r.Reason,
        Notes = r.Notes,
    };
}

public sealed class MedicalRecordInputValidator : AbstractValidator<MedicalRecordInput>
{
    public MedicalRecordInputValidator()
    {
        RuleFor(x => x.VisitDate).NotEmpty().WithName("Fecha de la visita");
        RuleFor(x => x.DoctorId).GreaterThan(0).When(x => x.DoctorId is not null).WithName("Médico");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500).WithName("Motivo");
        RuleFor(x => x.Notes).MaximumLength(4000).WithName("Notas");
    }
}

public interface IMedicalRecordService
{
    Task<IReadOnlyList<MedicalRecordDto>> ListByPatientAsync(int patientId, CancellationToken ct);
    Task<MedicalRecordDto> CreateAsync(int patientId, MedicalRecordInput input, CancellationToken ct);
    Task<MedicalRecordDto> UpdateAsync(int id, MedicalRecordInput input, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}

public sealed class MedicalRecordService(IAppDbContext db) : IMedicalRecordService
{
    private const string NotFoundMessage = "Registro médico no encontrado.";

    public async Task<IReadOnlyList<MedicalRecordDto>> ListByPatientAsync(int patientId, CancellationToken ct)
    {
        await PatientService.EnsureExistsAsync(db, patientId, ct);
        return await db.MedicalRecords.AsNoTracking()
            .Where(r => r.PatientId == patientId)
            .OrderByDescending(r => r.VisitDate).ThenByDescending(r => r.Id)
            .Select(MedicalRecordDto.Projection)
            .ToListAsync(ct);
    }

    public async Task<MedicalRecordDto> CreateAsync(int patientId, MedicalRecordInput input, CancellationToken ct)
    {
        await PatientService.EnsureExistsAsync(db, patientId, ct);
        await DoctorService.EnsureReferenceAsync(db, input.DoctorId, ct);
        var record = new MedicalRecord { PatientId = patientId };
        Apply(record, input);
        db.MedicalRecords.Add(record);
        await db.SaveChangesAsync(ct);
        return await GetAsync(record.Id, ct);
    }

    public async Task<MedicalRecordDto> UpdateAsync(int id, MedicalRecordInput input, CancellationToken ct)
    {
        var record = await db.MedicalRecords.FirstOrDefaultAsync(r => r.Id == id, ct)
                     ?? throw new NotFoundException(NotFoundMessage);
        await DoctorService.EnsureReferenceAsync(db, input.DoctorId, ct);
        Apply(record, input);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        if (await db.MedicalRecords.Where(r => r.Id == id).ExecuteDeleteAsync(ct) == 0)
            throw new NotFoundException(NotFoundMessage);
    }

    private async Task<MedicalRecordDto> GetAsync(int id, CancellationToken ct) =>
        await db.MedicalRecords.AsNoTracking().Where(r => r.Id == id).Select(MedicalRecordDto.Projection).FirstAsync(ct);

    private static void Apply(MedicalRecord r, MedicalRecordInput i)
    {
        r.VisitDate = DateTimeUtc.Normalize(i.VisitDate);
        r.DoctorId = i.DoctorId;
        r.Reason = i.Reason.Trim();
        r.Notes = i.Notes.NullIfBlank();
    }
}

// ---------- Diagnoses ----------

public class DiagnosisInput
{
    public string? Code { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateOnly DiagnosedAt { get; set; }
    public DiagnosisStatus Status { get; set; }
    public int? DoctorId { get; set; }
    public string? Notes { get; set; }
}

public class DiagnosisDto : DiagnosisInput
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public string? DoctorName { get; set; }

    public static readonly Expression<Func<Diagnosis, DiagnosisDto>> Projection = d => new DiagnosisDto
    {
        Id = d.Id,
        PatientId = d.PatientId,
        Code = d.Code,
        Description = d.Description,
        DiagnosedAt = d.DiagnosedAt,
        Status = d.Status,
        DoctorId = d.DoctorId,
        DoctorName = d.Doctor == null ? null : d.Doctor.FirstName + " " + d.Doctor.LastName,
        Notes = d.Notes,
    };
}

public sealed class DiagnosisInputValidator : AbstractValidator<DiagnosisInput>
{
    public DiagnosisInputValidator()
    {
        RuleFor(x => x.Code).MaximumLength(20).WithName("Código");
        RuleFor(x => x.Description).NotEmpty().MaximumLength(1000).WithName("Descripción");
        RuleFor(x => x.DiagnosedAt).NotEmpty().WithName("Fecha de diagnóstico");
        RuleFor(x => x.Status).IsInEnum().WithName("Estado");
        RuleFor(x => x.DoctorId).GreaterThan(0).When(x => x.DoctorId is not null).WithName("Médico");
        RuleFor(x => x.Notes).MaximumLength(4000).WithName("Notas");
    }
}

public interface IDiagnosisService
{
    Task<IReadOnlyList<DiagnosisDto>> ListByPatientAsync(int patientId, CancellationToken ct);
    Task<DiagnosisDto> CreateAsync(int patientId, DiagnosisInput input, CancellationToken ct);
    Task<DiagnosisDto> UpdateAsync(int id, DiagnosisInput input, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}

public sealed class DiagnosisService(IAppDbContext db) : IDiagnosisService
{
    private const string NotFoundMessage = "Diagnóstico no encontrado.";

    public async Task<IReadOnlyList<DiagnosisDto>> ListByPatientAsync(int patientId, CancellationToken ct)
    {
        await PatientService.EnsureExistsAsync(db, patientId, ct);
        return await db.Diagnoses.AsNoTracking()
            .Where(d => d.PatientId == patientId)
            .OrderByDescending(d => d.DiagnosedAt).ThenByDescending(d => d.Id)
            .Select(DiagnosisDto.Projection)
            .ToListAsync(ct);
    }

    public async Task<DiagnosisDto> CreateAsync(int patientId, DiagnosisInput input, CancellationToken ct)
    {
        await PatientService.EnsureExistsAsync(db, patientId, ct);
        await DoctorService.EnsureReferenceAsync(db, input.DoctorId, ct);
        var diagnosis = new Diagnosis { PatientId = patientId };
        Apply(diagnosis, input);
        db.Diagnoses.Add(diagnosis);
        await db.SaveChangesAsync(ct);
        return await GetAsync(diagnosis.Id, ct);
    }

    public async Task<DiagnosisDto> UpdateAsync(int id, DiagnosisInput input, CancellationToken ct)
    {
        var diagnosis = await db.Diagnoses.FirstOrDefaultAsync(d => d.Id == id, ct)
                        ?? throw new NotFoundException(NotFoundMessage);
        await DoctorService.EnsureReferenceAsync(db, input.DoctorId, ct);
        Apply(diagnosis, input);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Deletes the diagnosis and detaches any treatment that referenced it (FK is NO ACTION in the DB).</summary>
    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Treatments.IgnoreQueryFilters().Where(t => t.DiagnosisId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.DiagnosisId, (int?)null), ct);
        if (await db.Diagnoses.Where(d => d.Id == id).ExecuteDeleteAsync(ct) == 0)
            throw new NotFoundException(NotFoundMessage);
        await tx.CommitAsync(ct);
    }

    private async Task<DiagnosisDto> GetAsync(int id, CancellationToken ct) =>
        await db.Diagnoses.AsNoTracking().Where(d => d.Id == id).Select(DiagnosisDto.Projection).FirstAsync(ct);

    private static void Apply(Diagnosis d, DiagnosisInput i)
    {
        d.Code = i.Code.NullIfBlank();
        d.Description = i.Description.Trim();
        d.DiagnosedAt = i.DiagnosedAt;
        d.Status = i.Status;
        d.DoctorId = i.DoctorId;
        d.Notes = i.Notes.NullIfBlank();
    }
}

// ---------- Treatments ----------

public class TreatmentInput
{
    public string Description { get; set; } = string.Empty;
    public int? DiagnosisId { get; set; }
    public int? MedicationId { get; set; }
    public int? DoctorId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Instructions { get; set; }
    public TreatmentStatus Status { get; set; }
}

public class TreatmentDto : TreatmentInput
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public string? DiagnosisDescription { get; set; }
    public string? MedicationName { get; set; }
    public string? DoctorName { get; set; }

    public static readonly Expression<Func<Treatment, TreatmentDto>> Projection = t => new TreatmentDto
    {
        Id = t.Id,
        PatientId = t.PatientId,
        Description = t.Description,
        DiagnosisId = t.DiagnosisId,
        DiagnosisDescription = t.Diagnosis == null ? null : t.Diagnosis.Description,
        MedicationId = t.MedicationId,
        MedicationName = t.Medication == null ? null : t.Medication.CompoundName,
        DoctorId = t.DoctorId,
        DoctorName = t.Doctor == null ? null : t.Doctor.FirstName + " " + t.Doctor.LastName,
        StartDate = t.StartDate,
        EndDate = t.EndDate,
        Instructions = t.Instructions,
        Status = t.Status,
    };
}

public sealed class TreatmentInputValidator : AbstractValidator<TreatmentInput>
{
    public TreatmentInputValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(1000).WithName("Descripción");
        RuleFor(x => x.DiagnosisId).GreaterThan(0).When(x => x.DiagnosisId is not null).WithName("Diagnóstico");
        RuleFor(x => x.MedicationId).GreaterThan(0).When(x => x.MedicationId is not null).WithName("Medicamento");
        RuleFor(x => x.DoctorId).GreaterThan(0).When(x => x.DoctorId is not null).WithName("Médico");
        RuleFor(x => x.StartDate).NotEmpty().WithName("Fecha de inicio");
        RuleFor(x => x.EndDate)
            .Must((x, end) => end is null || end.Value >= x.StartDate)
            .WithMessage("La fecha de fin no puede ser anterior a la fecha de inicio.");
        RuleFor(x => x.Instructions).MaximumLength(2000).WithName("Indicaciones");
        RuleFor(x => x.Status).IsInEnum().WithName("Estado");
    }
}

public interface ITreatmentService
{
    Task<IReadOnlyList<TreatmentDto>> ListByPatientAsync(int patientId, CancellationToken ct);
    Task<TreatmentDto> CreateAsync(int patientId, TreatmentInput input, CancellationToken ct);
    Task<TreatmentDto> UpdateAsync(int id, TreatmentInput input, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}

public sealed class TreatmentService(IAppDbContext db) : ITreatmentService
{
    private const string NotFoundMessage = "Tratamiento no encontrado.";

    public async Task<IReadOnlyList<TreatmentDto>> ListByPatientAsync(int patientId, CancellationToken ct)
    {
        await PatientService.EnsureExistsAsync(db, patientId, ct);
        return await db.Treatments.AsNoTracking()
            .Where(t => t.PatientId == patientId)
            .OrderByDescending(t => t.StartDate).ThenByDescending(t => t.Id)
            .Select(TreatmentDto.Projection)
            .ToListAsync(ct);
    }

    public async Task<TreatmentDto> CreateAsync(int patientId, TreatmentInput input, CancellationToken ct)
    {
        await PatientService.EnsureExistsAsync(db, patientId, ct);
        await ValidateReferencesAsync(patientId, input, ct);
        var treatment = new Treatment { PatientId = patientId };
        Apply(treatment, input);
        db.Treatments.Add(treatment);
        await db.SaveChangesAsync(ct);
        return await GetAsync(treatment.Id, ct);
    }

    public async Task<TreatmentDto> UpdateAsync(int id, TreatmentInput input, CancellationToken ct)
    {
        var treatment = await db.Treatments.FirstOrDefaultAsync(t => t.Id == id, ct)
                        ?? throw new NotFoundException(NotFoundMessage);
        await ValidateReferencesAsync(treatment.PatientId, input, ct);
        Apply(treatment, input);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        if (await db.Treatments.Where(t => t.Id == id).ExecuteDeleteAsync(ct) == 0)
            throw new NotFoundException(NotFoundMessage);
    }

    private async Task ValidateReferencesAsync(int patientId, TreatmentInput input, CancellationToken ct)
    {
        await DoctorService.EnsureReferenceAsync(db, input.DoctorId, ct);
        if (input.DiagnosisId is { } diagnosisId &&
            !await db.Diagnoses.AnyAsync(d => d.Id == diagnosisId && d.PatientId == patientId, ct))
            throw new BusinessValidationException("diagnosisId", "El diagnóstico indicado no existe o no pertenece al paciente.");
        if (input.MedicationId is { } medicationId && !await db.Medications.AnyAsync(m => m.Id == medicationId, ct))
            throw new BusinessValidationException("medicationId", "El medicamento indicado no existe.");
    }

    private async Task<TreatmentDto> GetAsync(int id, CancellationToken ct) =>
        await db.Treatments.AsNoTracking().Where(t => t.Id == id).Select(TreatmentDto.Projection).FirstAsync(ct);

    private static void Apply(Treatment t, TreatmentInput i)
    {
        t.Description = i.Description.Trim();
        t.DiagnosisId = i.DiagnosisId;
        t.MedicationId = i.MedicationId;
        t.DoctorId = i.DoctorId;
        t.StartDate = i.StartDate;
        t.EndDate = i.EndDate;
        t.Instructions = i.Instructions.NullIfBlank();
        t.Status = i.Status;
    }
}
