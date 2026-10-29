using System.Linq.Expressions;
using FluentValidation;
using Medical.Application.Common;
using Medical.Domain.Entities;
using Medical.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Medical.Application.Billing;

public class PaymentPlanInput
{
    public int PatientId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal DownPayment { get; set; }
    public int NumberOfInstallments { get; set; }
    public PaymentFrequency Frequency { get; set; }
    public DateOnly StartDate { get; set; }
    public string? Terms { get; set; }
    public PaymentPlanStatus Status { get; set; }
}

public class PaymentPlanDto : PaymentPlanInput
{
    public int Id { get; set; }
    public string PatientName { get; set; } = string.Empty;

    /// <summary>Down payment plus the sum of all registered payments.</summary>
    public decimal PaidAmount { get; set; }
    public decimal Balance { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Regular installment amount (the last installment may differ by rounding).</summary>
    public decimal InstallmentAmount =>
        InstallmentCalculator.RegularAmount(TotalAmount, DownPayment, NumberOfInstallments);

    public static readonly Expression<Func<PaymentPlan, PaymentPlanDto>> Projection = p => new PaymentPlanDto
    {
        Id = p.Id,
        PatientId = p.PatientId,
        PatientName = p.Patient.FirstName + " " + p.Patient.LastName,
        Description = p.Description,
        TotalAmount = p.TotalAmount,
        DownPayment = p.DownPayment,
        NumberOfInstallments = p.NumberOfInstallments,
        Frequency = p.Frequency,
        StartDate = p.StartDate,
        Terms = p.Terms,
        Status = p.Status,
        PaidAmount = p.DownPayment + (p.Payments.Sum(x => (decimal?)x.Amount) ?? 0m),
        Balance = p.TotalAmount - p.DownPayment - (p.Payments.Sum(x => (decimal?)x.Amount) ?? 0m),
        CreatedAt = p.CreatedAt,
    };
}

public sealed record InstallmentDto(int Number, DateOnly DueDate, decimal Amount);

public class PaymentPlanDetailDto : PaymentPlanDto
{
    public IReadOnlyList<InstallmentDto> Installments =>
        InstallmentCalculator.Schedule(TotalAmount, DownPayment, NumberOfInstallments, Frequency, StartDate);

    public IReadOnlyList<PaymentDto> Payments { get; set; } = [];
}

public class PaymentInput
{
    public decimal Amount { get; set; }
    public DateOnly PaidAt { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
}

public class PaymentDto : PaymentInput
{
    public int Id { get; set; }
    public int PaymentPlanId { get; set; }
    public string? CreatedBy { get; set; }

    public static readonly Expression<Func<Payment, PaymentDto>> Projection = p => new PaymentDto
    {
        Id = p.Id,
        PaymentPlanId = p.PaymentPlanId,
        Amount = p.Amount,
        PaidAt = p.PaidAt,
        Method = p.Method,
        Reference = p.Reference,
        CreatedBy = p.CreatedBy,
    };
}

public sealed class PaymentPlanQuery : PageQuery
{
    public PaymentPlanStatus? Status { get; set; }
}

/// <summary>
/// Installments are derived, never stored: the financed amount (total − down payment) is split
/// into N equal installments truncated to cents, and the last one absorbs the remainder.
/// Installment k (1-based) is due at startDate + (k − 1) periods.
/// </summary>
public static class InstallmentCalculator
{
    public static decimal RegularAmount(decimal total, decimal downPayment, int count)
    {
        if (count <= 0) return 0m;
        var financed = Math.Max(total - downPayment, 0m);
        return Math.Floor(financed / count * 100m) / 100m;
    }

    public static IReadOnlyList<InstallmentDto> Schedule(
        decimal total, decimal downPayment, int count, PaymentFrequency frequency, DateOnly startDate)
    {
        if (count <= 0) return [];
        var financed = Math.Max(total - downPayment, 0m);
        var regular = RegularAmount(total, downPayment, count);
        var result = new List<InstallmentDto>(count);
        for (var k = 1; k <= count; k++)
        {
            var amount = k == count ? financed - regular * (count - 1) : regular;
            result.Add(new InstallmentDto(k, DueDate(startDate, frequency, k - 1), amount));
        }
        return result;
    }

    public static DateOnly DueDate(DateOnly start, PaymentFrequency frequency, int periods) => frequency switch
    {
        PaymentFrequency.Weekly => start.AddDays(7 * periods),
        PaymentFrequency.Biweekly => start.AddDays(14 * periods),
        _ => start.AddMonths(periods), // computed from start each time so month-end dates don't drift
    };
}

public sealed class PaymentPlanInputValidator : AbstractValidator<PaymentPlanInput>
{
    public PaymentPlanInputValidator()
    {
        RuleFor(x => x.PatientId).GreaterThan(0).WithName("Paciente");
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500).WithName("Descripción");
        RuleFor(x => x.TotalAmount).GreaterThan(0).PrecisionScale(18, 2, true).WithName("Monto total");
        RuleFor(x => x.DownPayment).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, true).WithName("Prima");
        RuleFor(x => x.DownPayment).LessThanOrEqualTo(x => x.TotalAmount)
            .WithMessage("La prima no puede ser mayor que el monto total.");
        RuleFor(x => x.NumberOfInstallments).InclusiveBetween(1, 120).WithName("Número de cuotas");
        RuleFor(x => x.Frequency).IsInEnum().WithName("Frecuencia");
        RuleFor(x => x.StartDate).NotEmpty().WithName("Fecha de inicio");
        RuleFor(x => x.Terms).MaximumLength(4000).WithName("Términos");
        RuleFor(x => x.Status).IsInEnum().WithName("Estado");
    }
}

public sealed class PaymentInputValidator : AbstractValidator<PaymentInput>
{
    public PaymentInputValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(18, 2, true).WithName("Monto");
        RuleFor(x => x.PaidAt).NotEmpty().WithName("Fecha de pago");
        RuleFor(x => x.Method).IsInEnum().WithName("Método de pago");
        RuleFor(x => x.Reference).MaximumLength(100).WithName("Referencia");
    }
}

public interface IPaymentPlanService
{
    Task<PagedResult<PaymentPlanDto>> ListAsync(PaymentPlanQuery query, CancellationToken ct);
    Task<IReadOnlyList<PaymentPlanDto>> ListByPatientAsync(int patientId, CancellationToken ct);
    Task<PaymentPlanDetailDto> GetAsync(int id, CancellationToken ct);
    Task<PaymentPlanDto> CreateAsync(PaymentPlanInput input, CancellationToken ct);
    Task<PaymentPlanDto> UpdateAsync(int id, PaymentPlanInput input, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
    Task<PaymentDto> AddPaymentAsync(int planId, PaymentInput input, CancellationToken ct);
    Task DeletePaymentAsync(int paymentId, CancellationToken ct);
}

public sealed class PaymentPlanService(IAppDbContext db) : IPaymentPlanService
{
    private const string NotFoundMessage = "Plan de pago no encontrado.";

    public Task<PagedResult<PaymentPlanDto>> ListAsync(PaymentPlanQuery query, CancellationToken ct)
    {
        var plans = db.PaymentPlans.AsNoTracking();
        if (query.Status is { } status) plans = plans.Where(p => p.Status == status);
        if (query.NormalizedSearch is { } s)
        {
            plans = plans.Where(p =>
                (p.Patient.FirstName + " " + p.Patient.LastName).Contains(s) ||
                p.Patient.DocumentId.Contains(s) || p.Description.Contains(s));
        }

        return plans.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            .Select(PaymentPlanDto.Projection)
            .ToPagedAsync(query, ct);
    }

    public async Task<IReadOnlyList<PaymentPlanDto>> ListByPatientAsync(int patientId, CancellationToken ct)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == patientId, ct))
            throw new NotFoundException("Paciente no encontrado.");
        return await db.PaymentPlans.AsNoTracking()
            .Where(p => p.PatientId == patientId)
            .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            .Select(PaymentPlanDto.Projection)
            .ToListAsync(ct);
    }

    public async Task<PaymentPlanDetailDto> GetAsync(int id, CancellationToken ct)
    {
        var plan = await db.PaymentPlans.AsNoTracking()
                       .Where(p => p.Id == id)
                       .Select(p => new PaymentPlanDetailDto
                       {
                           Id = p.Id,
                           PatientId = p.PatientId,
                           PatientName = p.Patient.FirstName + " " + p.Patient.LastName,
                           Description = p.Description,
                           TotalAmount = p.TotalAmount,
                           DownPayment = p.DownPayment,
                           NumberOfInstallments = p.NumberOfInstallments,
                           Frequency = p.Frequency,
                           StartDate = p.StartDate,
                           Terms = p.Terms,
                           Status = p.Status,
                           PaidAmount = p.DownPayment + (p.Payments.Sum(x => (decimal?)x.Amount) ?? 0m),
                           Balance = p.TotalAmount - p.DownPayment - (p.Payments.Sum(x => (decimal?)x.Amount) ?? 0m),
                           CreatedAt = p.CreatedAt,
                       })
                       .FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(NotFoundMessage);

        plan.Payments = await db.Payments.AsNoTracking()
            .Where(x => x.PaymentPlanId == id)
            .OrderBy(x => x.PaidAt).ThenBy(x => x.Id)
            .Select(PaymentDto.Projection)
            .ToListAsync(ct);
        return plan;
    }

    public async Task<PaymentPlanDto> CreateAsync(PaymentPlanInput input, CancellationToken ct)
    {
        await EnsurePatientAsync(input.PatientId, ct);
        var plan = new PaymentPlan();
        Apply(plan, input);
        plan.Status = ResolveStatus(input.Status, input.TotalAmount - input.DownPayment);
        db.PaymentPlans.Add(plan);
        await db.SaveChangesAsync(ct);
        return await GetSummaryAsync(plan.Id, ct);
    }

    public async Task<PaymentPlanDto> UpdateAsync(int id, PaymentPlanInput input, CancellationToken ct)
    {
        var plan = await db.PaymentPlans.FirstOrDefaultAsync(p => p.Id == id, ct)
                   ?? throw new NotFoundException(NotFoundMessage);
        if (plan.PatientId != input.PatientId) await EnsurePatientAsync(input.PatientId, ct);

        var paymentsTotal = await db.Payments.Where(x => x.PaymentPlanId == id).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        var balance = input.TotalAmount - input.DownPayment - paymentsTotal;
        if (balance < 0)
            throw new BusinessValidationException("totalAmount", "El monto total no puede ser menor que el monto ya pagado.");

        Apply(plan, input);
        plan.Status = ResolveStatus(input.Status, balance);
        await db.SaveChangesAsync(ct);
        return await GetSummaryAsync(id, ct);
    }

    /// <summary>Hard delete; payments cascade in the database.</summary>
    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        if (await db.PaymentPlans.Where(p => p.Id == id).ExecuteDeleteAsync(ct) == 0)
            throw new NotFoundException(NotFoundMessage);
    }

    public async Task<PaymentDto> AddPaymentAsync(int planId, PaymentInput input, CancellationToken ct)
    {
        var plan = await db.PaymentPlans.FirstOrDefaultAsync(p => p.Id == planId, ct)
                   ?? throw new NotFoundException(NotFoundMessage);
        if (plan.Status == PaymentPlanStatus.Cancelled)
            throw new BusinessValidationException("paymentPlanId", "No se pueden registrar pagos en un plan cancelado.");

        var paymentsTotal = await db.Payments.Where(x => x.PaymentPlanId == planId).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        var balance = plan.TotalAmount - plan.DownPayment - paymentsTotal;
        if (input.Amount > balance)
            throw new BusinessValidationException("amount", $"El monto excede el saldo pendiente ({balance:0.00}).");

        var payment = new Payment
        {
            PaymentPlanId = planId,
            Amount = input.Amount,
            PaidAt = input.PaidAt,
            Method = input.Method,
            Reference = input.Reference.NullIfBlank(),
        };
        db.Payments.Add(payment);

        // Touching the plan bumps its RowVersion, so two concurrent payments against the same
        // balance cannot both commit: the loser gets a DbUpdateConcurrencyException (→ 409).
        plan.Status = balance - input.Amount <= 0 ? PaymentPlanStatus.Completed : PaymentPlanStatus.Active;
        plan.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return await db.Payments.AsNoTracking().Where(x => x.Id == payment.Id).Select(PaymentDto.Projection).FirstAsync(ct);
    }

    public async Task DeletePaymentAsync(int paymentId, CancellationToken ct)
    {
        var payment = await db.Payments.Include(x => x.PaymentPlan).FirstOrDefaultAsync(x => x.Id == paymentId, ct)
                      ?? throw new NotFoundException("Pago no encontrado.");
        var plan = payment.PaymentPlan;
        db.Payments.Remove(payment);
        if (plan.Status == PaymentPlanStatus.Completed && payment.Amount > 0)
            plan.Status = PaymentPlanStatus.Active;
        plan.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task<PaymentPlanDto> GetSummaryAsync(int id, CancellationToken ct) =>
        await db.PaymentPlans.AsNoTracking().Where(p => p.Id == id).Select(PaymentPlanDto.Projection).FirstAsync(ct);

    private async Task EnsurePatientAsync(int patientId, CancellationToken ct)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == patientId, ct))
            throw new BusinessValidationException("patientId", "El paciente indicado no existe.");
    }

    /// <summary>Cancelled is sticky; otherwise the status follows the balance.</summary>
    private static PaymentPlanStatus ResolveStatus(PaymentPlanStatus requested, decimal balance) =>
        requested == PaymentPlanStatus.Cancelled ? PaymentPlanStatus.Cancelled
        : balance <= 0 ? PaymentPlanStatus.Completed
        : PaymentPlanStatus.Active;

    private static void Apply(PaymentPlan p, PaymentPlanInput i)
    {
        p.PatientId = i.PatientId;
        p.Description = i.Description.Trim();
        p.TotalAmount = i.TotalAmount;
        p.DownPayment = i.DownPayment;
        p.NumberOfInstallments = i.NumberOfInstallments;
        p.Frequency = i.Frequency;
        p.StartDate = i.StartDate;
        p.Terms = i.Terms.NullIfBlank();
        p.Status = i.Status;
    }
}
