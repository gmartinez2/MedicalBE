using System.Linq.Expressions;
using FluentValidation;
using Medical.Application.Common;
using Medical.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Medical.Application.Medications;

public class MedicationInput
{
    public string CompoundName { get; set; } = string.Empty;
    public string Dose { get; set; } = string.Empty;
    public string PharmaceuticalCompany { get; set; } = string.Empty;
    public string? Presentation { get; set; }
    public int Stock { get; set; }
    public int MinimumStock { get; set; }
}

public class MedicationDto : MedicationInput
{
    public int Id { get; set; }

    /// <summary>True when stock is at or below the configured minimum.</summary>
    public bool IsLowStock { get; set; }

    /// <summary>SQL-translatable low-stock rule, shared with the dashboard.</summary>
    public static readonly Expression<Func<Medication, bool>> LowStock = m => m.Stock <= m.MinimumStock;

    public static readonly Expression<Func<Medication, MedicationDto>> Projection = m => new MedicationDto
    {
        Id = m.Id,
        CompoundName = m.CompoundName,
        Dose = m.Dose,
        PharmaceuticalCompany = m.PharmaceuticalCompany,
        Presentation = m.Presentation,
        Stock = m.Stock,
        MinimumStock = m.MinimumStock,
        IsLowStock = m.Stock <= m.MinimumStock,
    };
}

public sealed class MedicationInputValidator : AbstractValidator<MedicationInput>
{
    public MedicationInputValidator()
    {
        RuleFor(x => x.CompoundName).NotEmpty().MaximumLength(200).WithName("Compuesto");
        RuleFor(x => x.Dose).NotEmpty().MaximumLength(100).WithName("Dosis");
        RuleFor(x => x.PharmaceuticalCompany).NotEmpty().MaximumLength(200).WithName("Farmacéutica");
        RuleFor(x => x.Presentation).MaximumLength(200).WithName("Presentación");
        RuleFor(x => x.Stock).GreaterThanOrEqualTo(0).WithName("Existencias");
        RuleFor(x => x.MinimumStock).GreaterThanOrEqualTo(0).WithName("Existencia mínima");
    }
}

public interface IMedicationService
{
    Task<PagedResult<MedicationDto>> ListAsync(PageQuery query, CancellationToken ct);
    Task<MedicationDto> GetAsync(int id, CancellationToken ct);
    Task<MedicationDto> CreateAsync(MedicationInput input, CancellationToken ct);
    Task<MedicationDto> UpdateAsync(int id, MedicationInput input, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}

public sealed class MedicationService(IAppDbContext db) : IMedicationService
{
    private const string NotFoundMessage = "Medicamento no encontrado.";

    public Task<PagedResult<MedicationDto>> ListAsync(PageQuery query, CancellationToken ct)
    {
        var meds = db.Medications.AsNoTracking();
        if (query.NormalizedSearch is { } s)
            meds = meds.Where(m => m.CompoundName.Contains(s) || m.PharmaceuticalCompany.Contains(s));

        return meds.OrderBy(m => m.CompoundName).ThenBy(m => m.Id)
            .Select(MedicationDto.Projection)
            .ToPagedAsync(query, ct);
    }

    public async Task<MedicationDto> GetAsync(int id, CancellationToken ct) =>
        await db.Medications.AsNoTracking().Where(m => m.Id == id).Select(MedicationDto.Projection).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException(NotFoundMessage);

    public async Task<MedicationDto> CreateAsync(MedicationInput input, CancellationToken ct)
    {
        var med = new Medication();
        Apply(med, input);
        db.Medications.Add(med);
        await db.SaveChangesAsync(ct);
        return await GetAsync(med.Id, ct);
    }

    public async Task<MedicationDto> UpdateAsync(int id, MedicationInput input, CancellationToken ct)
    {
        var med = await db.Medications.FirstOrDefaultAsync(m => m.Id == id, ct)
                  ?? throw new NotFoundException(NotFoundMessage);
        Apply(med, input);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Deletes the medication and detaches treatments that referenced it (FK is NO ACTION in the DB).</summary>
    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Treatments.IgnoreQueryFilters().Where(t => t.MedicationId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.MedicationId, (int?)null), ct);
        if (await db.Medications.Where(m => m.Id == id).ExecuteDeleteAsync(ct) == 0)
            throw new NotFoundException(NotFoundMessage);
        await tx.CommitAsync(ct);
    }

    private static void Apply(Medication m, MedicationInput i)
    {
        m.CompoundName = i.CompoundName.Trim();
        m.Dose = i.Dose.Trim();
        m.PharmaceuticalCompany = i.PharmaceuticalCompany.Trim();
        m.Presentation = i.Presentation.NullIfBlank();
        m.Stock = i.Stock;
        m.MinimumStock = i.MinimumStock;
    }
}
