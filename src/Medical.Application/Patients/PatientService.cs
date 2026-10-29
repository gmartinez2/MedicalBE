using System.Linq.Expressions;
using FluentValidation;
using Medical.Application.Common;
using Medical.Domain.Entities;
using Medical.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Medical.Application.Patients;

public class PatientInput
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public Gender Gender { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? BloodType { get; set; }
    public string? Allergies { get; set; }
}

public class PatientDto : PatientInput
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    /// <summary>Age in whole years, computed at serialization time from <see cref="PatientInput.DateOfBirth"/>.</summary>
    public int Age => AgeCalculator.YearsOld(DateOfBirth, DateOnly.FromDateTime(DateTime.UtcNow));

    public static readonly Expression<Func<Patient, PatientDto>> Projection = p => new PatientDto
    {
        Id = p.Id,
        FirstName = p.FirstName,
        LastName = p.LastName,
        FullName = p.FirstName + " " + p.LastName,
        DocumentId = p.DocumentId,
        DateOfBirth = p.DateOfBirth,
        Gender = p.Gender,
        Phone = p.Phone,
        Email = p.Email,
        Address = p.Address,
        BloodType = p.BloodType,
        Allergies = p.Allergies,
        CreatedAt = p.CreatedAt,
    };
}

public static class AgeCalculator
{
    public static int YearsOld(DateOnly dateOfBirth, DateOnly today)
    {
        var age = today.Year - dateOfBirth.Year;
        if (dateOfBirth > today.AddYears(-age)) age--;
        return Math.Max(age, 0);
    }
}

public sealed class PatientInputValidator : AbstractValidator<PatientInput>
{
    public PatientInputValidator(TimeProvider clock)
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100).WithName("Nombre");
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100).WithName("Apellido");
        RuleFor(x => x.DocumentId).NotEmpty().MaximumLength(50).WithName("Número de documento");
        RuleFor(x => x.DateOfBirth)
            .Must(d => d >= new DateOnly(1900, 1, 1) && d <= DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
            .WithMessage("La fecha de nacimiento debe estar entre 1900-01-01 y la fecha actual.");
        RuleFor(x => x.Gender).IsInEnum().WithName("Género");
        RuleFor(x => x.Phone).MaximumLength(30).WithName("Teléfono");
        RuleFor(x => x.Email).MaximumLength(256).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithName("Correo electrónico");
        RuleFor(x => x.Address).MaximumLength(300).WithName("Dirección");
        RuleFor(x => x.BloodType).MaximumLength(10).WithName("Tipo de sangre");
        RuleFor(x => x.Allergies).MaximumLength(1000).WithName("Alergias");
    }
}

public interface IPatientService
{
    Task<PagedResult<PatientDto>> ListAsync(PageQuery query, CancellationToken ct);
    Task<PatientDto> GetAsync(int id, CancellationToken ct);
    Task<PatientDto> CreateAsync(PatientInput input, CancellationToken ct);
    Task<PatientDto> UpdateAsync(int id, PatientInput input, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}

public sealed class PatientService(IAppDbContext db, TimeProvider clock) : IPatientService
{
    internal const string NotFoundMessage = "Paciente no encontrado.";
    private const string DuplicateDocumentMessage = "Ya existe un paciente con ese número de documento.";

    public Task<PagedResult<PatientDto>> ListAsync(PageQuery query, CancellationToken ct)
    {
        var patients = db.Patients.AsNoTracking();
        if (query.NormalizedSearch is { } s)
        {
            patients = patients.Where(p =>
                p.FirstName.Contains(s) || p.LastName.Contains(s) || p.DocumentId.Contains(s) ||
                (p.FirstName + " " + p.LastName).Contains(s));
        }

        return patients
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ThenBy(p => p.Id)
            .Select(PatientDto.Projection)
            .ToPagedAsync(query, ct);
    }

    public async Task<PatientDto> GetAsync(int id, CancellationToken ct) =>
        await db.Patients.AsNoTracking().Where(p => p.Id == id).Select(PatientDto.Projection).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException(NotFoundMessage);

    public async Task<PatientDto> CreateAsync(PatientInput input, CancellationToken ct)
    {
        var documentId = input.DocumentId.Trim();
        if (await db.Patients.AnyAsync(p => p.DocumentId == documentId, ct))
            throw new ConflictException(DuplicateDocumentMessage);

        var patient = new Patient();
        Apply(patient, input);
        db.Patients.Add(patient);
        await db.SaveChangesAsync(ct);
        return await GetAsync(patient.Id, ct);
    }

    public async Task<PatientDto> UpdateAsync(int id, PatientInput input, CancellationToken ct)
    {
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == id, ct)
                      ?? throw new NotFoundException(NotFoundMessage);

        var documentId = input.DocumentId.Trim();
        if (await db.Patients.AnyAsync(p => p.DocumentId == documentId && p.Id != id, ct))
            throw new ConflictException(DuplicateDocumentMessage);

        Apply(patient, input);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var affected = await db.Patients
            .Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDeleted, true).SetProperty(p => p.DeletedAt, now), ct);
        if (affected == 0) throw new NotFoundException(NotFoundMessage);
    }

    /// <summary>Throws 404 when the patient does not exist (or is soft-deleted).</summary>
    internal static async Task EnsureExistsAsync(IAppDbContext db, int patientId, CancellationToken ct)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == patientId, ct))
            throw new NotFoundException(NotFoundMessage);
    }

    private static void Apply(Patient p, PatientInput i)
    {
        p.FirstName = i.FirstName.Trim();
        p.LastName = i.LastName.Trim();
        p.DocumentId = i.DocumentId.Trim();
        p.DateOfBirth = i.DateOfBirth;
        p.Gender = i.Gender;
        p.Phone = i.Phone.NullIfBlank();
        p.Email = i.Email.NullIfBlank();
        p.Address = i.Address.NullIfBlank();
        p.BloodType = i.BloodType.NullIfBlank();
        p.Allergies = i.Allergies.NullIfBlank();
    }
}
