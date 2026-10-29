using System.Linq.Expressions;
using FluentValidation;
using Medical.Application.Common;
using Medical.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Medical.Application.Doctors;

public class ShiftInput
{
    public int DoctorId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string Location { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class ShiftDto : ShiftInput
{
    public int Id { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string Specialty { get; set; } = string.Empty;

    public static readonly Expression<Func<Shift, ShiftDto>> Projection = s => new ShiftDto
    {
        Id = s.Id,
        DoctorId = s.DoctorId,
        DoctorName = s.Doctor.FirstName + " " + s.Doctor.LastName,
        Specialty = s.Doctor.Specialty,
        StartTime = s.StartTime,
        EndTime = s.EndTime,
        Location = s.Location,
        Notes = s.Notes,
    };
}

public sealed class ShiftQuery
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int? DoctorId { get; set; }
}

public sealed class ShiftInputValidator : AbstractValidator<ShiftInput>
{
    public ShiftInputValidator()
    {
        RuleFor(x => x.DoctorId).GreaterThan(0).WithName("Médico");
        RuleFor(x => x.StartTime).NotEmpty().WithName("Hora de inicio");
        RuleFor(x => x.EndTime)
            .Must((x, end) => DateTimeUtc.Normalize(end) > DateTimeUtc.Normalize(x.StartTime))
            .WithMessage("La hora de fin debe ser posterior a la hora de inicio.");
        RuleFor(x => x.Location).NotEmpty().MaximumLength(200).WithName("Ubicación");
        RuleFor(x => x.Notes).MaximumLength(1000).WithName("Notas");
    }
}

public interface IShiftService
{
    Task<IReadOnlyList<ShiftDto>> ListAsync(ShiftQuery query, CancellationToken ct);
    Task<ShiftDto> GetAsync(int id, CancellationToken ct);
    Task<ShiftDto> CreateAsync(ShiftInput input, CancellationToken ct);
    Task<ShiftDto> UpdateAsync(int id, ShiftInput input, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}

public sealed class ShiftService(IAppDbContext db) : IShiftService
{
    private const string NotFoundMessage = "Turno no encontrado.";
    private const string OverlapMessage = "El turno se superpone con otro turno del médico.";

    public async Task<IReadOnlyList<ShiftDto>> ListAsync(ShiftQuery query, CancellationToken ct)
    {
        var shifts = db.Shifts.AsNoTracking();
        if (DateTimeUtc.Normalize(query.From) is { } from) shifts = shifts.Where(s => s.EndTime > from);
        if (DateTimeUtc.Normalize(query.To) is { } to) shifts = shifts.Where(s => s.StartTime < to);
        if (query.DoctorId is { } doctorId) shifts = shifts.Where(s => s.DoctorId == doctorId);

        return await shifts.OrderBy(s => s.StartTime).ThenBy(s => s.Id).Select(ShiftDto.Projection).ToListAsync(ct);
    }

    public async Task<ShiftDto> GetAsync(int id, CancellationToken ct) =>
        await db.Shifts.AsNoTracking().Where(s => s.Id == id).Select(ShiftDto.Projection).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException(NotFoundMessage);

    public async Task<ShiftDto> CreateAsync(ShiftInput input, CancellationToken ct)
    {
        var shift = new Shift();
        Apply(shift, input);
        await ValidateAsync(shift, excludeId: null, ct);
        db.Shifts.Add(shift);
        await db.SaveChangesAsync(ct);
        return await GetAsync(shift.Id, ct);
    }

    public async Task<ShiftDto> UpdateAsync(int id, ShiftInput input, CancellationToken ct)
    {
        var shift = await db.Shifts.FirstOrDefaultAsync(s => s.Id == id, ct)
                    ?? throw new NotFoundException(NotFoundMessage);
        Apply(shift, input);
        await ValidateAsync(shift, excludeId: id, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        if (await db.Shifts.Where(s => s.Id == id).ExecuteDeleteAsync(ct) == 0)
            throw new NotFoundException(NotFoundMessage);
    }

    private async Task ValidateAsync(Shift shift, int? excludeId, CancellationToken ct)
    {
        if (!await db.Doctors.AnyAsync(d => d.Id == shift.DoctorId, ct))
            throw new BusinessValidationException("doctorId", "El médico indicado no existe.");

        // Half-open intervals: a shift ending at 16:00 does not overlap one starting at 16:00.
        var overlaps = await db.Shifts.AnyAsync(s =>
            s.DoctorId == shift.DoctorId &&
            (excludeId == null || s.Id != excludeId) &&
            s.StartTime < shift.EndTime && s.EndTime > shift.StartTime, ct);
        if (overlaps) throw new ConflictException(OverlapMessage);
    }

    private static void Apply(Shift s, ShiftInput i)
    {
        s.DoctorId = i.DoctorId;
        s.StartTime = DateTimeUtc.Normalize(i.StartTime);
        s.EndTime = DateTimeUtc.Normalize(i.EndTime);
        s.Location = i.Location.Trim();
        s.Notes = i.Notes.NullIfBlank();
    }
}
