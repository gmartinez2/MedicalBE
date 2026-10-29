using System.Linq.Expressions;
using FluentValidation;
using Medical.Application.Common;
using Medical.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Medical.Application.Doctors;

public class DoctorInput
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Specialty { get; set; } = string.Empty;
    public string LicenseNumber { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
}

public class DoctorDto : DoctorInput
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;

    public static readonly Expression<Func<Doctor, DoctorDto>> Projection = d => new DoctorDto
    {
        Id = d.Id,
        FirstName = d.FirstName,
        LastName = d.LastName,
        FullName = d.FirstName + " " + d.LastName,
        Specialty = d.Specialty,
        LicenseNumber = d.LicenseNumber,
        Phone = d.Phone,
        Email = d.Email,
    };
}

public sealed class DoctorInputValidator : AbstractValidator<DoctorInput>
{
    public DoctorInputValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100).WithName("Nombre");
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100).WithName("Apellido");
        RuleFor(x => x.Specialty).NotEmpty().MaximumLength(100).WithName("Especialidad");
        RuleFor(x => x.LicenseNumber).NotEmpty().MaximumLength(50).WithName("Número de colegiado");
        RuleFor(x => x.Phone).MaximumLength(30).WithName("Teléfono");
        RuleFor(x => x.Email).MaximumLength(256).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithName("Correo electrónico");
    }
}

public interface IDoctorService
{
    Task<PagedResult<DoctorDto>> ListAsync(PageQuery query, CancellationToken ct);
    Task<DoctorDto> GetAsync(int id, CancellationToken ct);
    Task<DoctorDto> CreateAsync(DoctorInput input, CancellationToken ct);
    Task<DoctorDto> UpdateAsync(int id, DoctorInput input, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}

public sealed class DoctorService(IAppDbContext db) : IDoctorService
{
    private const string NotFoundMessage = "Médico no encontrado.";
    private const string DuplicateLicenseMessage = "Ya existe un médico con ese número de colegiado.";

    public Task<PagedResult<DoctorDto>> ListAsync(PageQuery query, CancellationToken ct)
    {
        var doctors = db.Doctors.AsNoTracking();
        if (query.NormalizedSearch is { } s)
        {
            doctors = doctors.Where(d =>
                d.FirstName.Contains(s) || d.LastName.Contains(s) || d.Specialty.Contains(s) ||
                d.LicenseNumber.Contains(s) || (d.FirstName + " " + d.LastName).Contains(s));
        }

        return doctors
            .OrderBy(d => d.LastName).ThenBy(d => d.FirstName).ThenBy(d => d.Id)
            .Select(DoctorDto.Projection)
            .ToPagedAsync(query, ct);
    }

    public async Task<DoctorDto> GetAsync(int id, CancellationToken ct) =>
        await db.Doctors.AsNoTracking().Where(d => d.Id == id).Select(DoctorDto.Projection).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException(NotFoundMessage);

    public async Task<DoctorDto> CreateAsync(DoctorInput input, CancellationToken ct)
    {
        var license = input.LicenseNumber.Trim();
        if (await db.Doctors.AnyAsync(d => d.LicenseNumber == license, ct))
            throw new ConflictException(DuplicateLicenseMessage);

        var doctor = new Doctor();
        Apply(doctor, input);
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync(ct);
        return await GetAsync(doctor.Id, ct);
    }

    public async Task<DoctorDto> UpdateAsync(int id, DoctorInput input, CancellationToken ct)
    {
        var doctor = await db.Doctors.FirstOrDefaultAsync(d => d.Id == id, ct)
                     ?? throw new NotFoundException(NotFoundMessage);
        var license = input.LicenseNumber.Trim();
        if (await db.Doctors.AnyAsync(d => d.LicenseNumber == license && d.Id != id, ct))
            throw new ConflictException(DuplicateLicenseMessage);

        Apply(doctor, input);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Hard delete. Shifts cascade in the database; clinical references (records, diagnoses,
    /// treatments) are nulled by the FK's ON DELETE SET NULL so history is preserved.
    /// </summary>
    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var affected = await db.Doctors.Where(d => d.Id == id).ExecuteDeleteAsync(ct);
        if (affected == 0) throw new NotFoundException(NotFoundMessage);
    }

    /// <summary>Throws a 400 (field <c>doctorId</c>) when an optional doctor reference does not exist.</summary>
    internal static async Task EnsureReferenceAsync(IAppDbContext db, int? doctorId, CancellationToken ct)
    {
        if (doctorId is { } idValue && !await db.Doctors.AnyAsync(d => d.Id == idValue, ct))
            throw new BusinessValidationException("doctorId", "El médico indicado no existe.");
    }

    private static void Apply(Doctor d, DoctorInput i)
    {
        d.FirstName = i.FirstName.Trim();
        d.LastName = i.LastName.Trim();
        d.Specialty = i.Specialty.Trim();
        d.LicenseNumber = i.LicenseNumber.Trim();
        d.Phone = i.Phone.NullIfBlank();
        d.Email = i.Email.NullIfBlank();
    }
}
