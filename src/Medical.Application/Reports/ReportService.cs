using Medical.Application.Billing;
using Medical.Application.Clinical;
using Medical.Application.Common;
using Medical.Application.Medications;
using Medical.Application.Patients;
using Medical.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Medical.Application.Reports;

public sealed record MedicalHistoryReportDto(
    PatientDto Patient,
    IReadOnlyList<MedicalRecordDto> Records,
    IReadOnlyList<DiagnosisDto> Diagnoses,
    IReadOnlyList<TreatmentDto> Treatments,
    IReadOnlyList<VitalSignDto> Vitals,
    IReadOnlyList<MedicalImageDto> Images);

public sealed record BalanceRowDto(
    int PatientId,
    string PatientName,
    string DocumentId,
    int ActivePlans,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal Balance,
    DateOnly? LastPaymentAt);

public sealed record BalanceTotalsDto(decimal TotalAmount, decimal PaidAmount, decimal Balance);

public sealed record BalanceReportDto(IReadOnlyList<BalanceRowDto> Rows, BalanceTotalsDto Totals);

public sealed record DashboardSummaryDto(
    int Patients,
    int DoctorsOnShiftNow,
    int ShiftsToday,
    int ActivePaymentPlans,
    decimal OutstandingBalance,
    int LowStockMedications,
    IReadOnlyList<VitalSignAlertDto> RecentVitalAlerts);

public interface IReportService
{
    Task<IReadOnlyList<MedicalHistoryReportDto>> MedicalHistoryAsync(string? patientName, CancellationToken ct);
    Task<BalanceReportDto> BalancesAsync(string? patientName, bool onlyWithBalance, CancellationToken ct);
    Task<DashboardSummaryDto> DashboardAsync(CancellationToken ct);
}

public sealed class ReportService(IAppDbContext db, TimeProvider clock) : IReportService
{
    /// <summary>Upper bound on patients returned by the medical history report (protects against "match everything").</summary>
    public const int MaxHistoryPatients = 50;
    private const int RecentAlertCount = 10;

    /// <summary>
    /// One query per section (patients, records, diagnoses, treatments, vitals, images) regardless of
    /// how many patients match — no N+1.
    /// </summary>
    public async Task<IReadOnlyList<MedicalHistoryReportDto>> MedicalHistoryAsync(string? patientName, CancellationToken ct)
    {
        var patientsQuery = db.Patients.AsNoTracking();
        if (patientName.NullIfBlank() is { } name)
            patientsQuery = patientsQuery.Where(p => (p.FirstName + " " + p.LastName).Contains(name));

        var patients = await patientsQuery
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ThenBy(p => p.Id)
            .Take(MaxHistoryPatients)
            .Select(PatientDto.Projection)
            .ToListAsync(ct);
        if (patients.Count == 0) return [];

        var ids = patients.Select(p => p.Id).ToList();

        var records = await db.MedicalRecords.AsNoTracking().Where(r => ids.Contains(r.PatientId))
            .OrderByDescending(r => r.VisitDate).Select(MedicalRecordDto.Projection).ToListAsync(ct);
        var diagnoses = await db.Diagnoses.AsNoTracking().Where(d => ids.Contains(d.PatientId))
            .OrderByDescending(d => d.DiagnosedAt).Select(DiagnosisDto.Projection).ToListAsync(ct);
        var treatments = await db.Treatments.AsNoTracking().Where(t => ids.Contains(t.PatientId))
            .OrderByDescending(t => t.StartDate).Select(TreatmentDto.Projection).ToListAsync(ct);
        var vitals = await db.VitalSigns.AsNoTracking().Where(v => ids.Contains(v.PatientId))
            .OrderByDescending(v => v.RecordedAt).Select(VitalSignDto.Projection).ToListAsync(ct);
        var images = await db.MedicalImages.AsNoTracking().Where(i => ids.Contains(i.PatientId))
            .OrderByDescending(i => i.StudyDate).Select(MedicalImageDto.Projection).ToListAsync(ct);

        var recordsBy = records.ToLookup(x => x.PatientId);
        var diagnosesBy = diagnoses.ToLookup(x => x.PatientId);
        var treatmentsBy = treatments.ToLookup(x => x.PatientId);
        var vitalsBy = vitals.ToLookup(x => x.PatientId);
        var imagesBy = images.ToLookup(x => x.PatientId);

        return patients.Select(p => new MedicalHistoryReportDto(
            p,
            recordsBy[p.Id].ToList(),
            diagnosesBy[p.Id].ToList(),
            treatmentsBy[p.Id].ToList(),
            vitalsBy[p.Id].ToList(),
            imagesBy[p.Id].ToList())).ToList();
    }

    /// <summary>
    /// Per-patient balance over non-cancelled plans. Aggregation is pushed to SQL as two GROUP BY
    /// queries (plans and payments) because SQL Server cannot aggregate over a nested aggregate.
    /// </summary>
    public async Task<BalanceReportDto> BalancesAsync(string? patientName, bool onlyWithBalance, CancellationToken ct)
    {
        var name = patientName.NullIfBlank();

        var plans = db.PaymentPlans.AsNoTracking().Where(p => p.Status != PaymentPlanStatus.Cancelled);
        if (name is not null) plans = plans.Where(p => (p.Patient.FirstName + " " + p.Patient.LastName).Contains(name));

        var planTotals = await plans
            .GroupBy(p => p.PatientId)
            .Select(g => new
            {
                PatientId = g.Key,
                Total = g.Sum(p => p.TotalAmount),
                Down = g.Sum(p => p.DownPayment),
                ActivePlans = g.Count(p => p.Status == PaymentPlanStatus.Active),
            })
            .ToListAsync(ct);
        if (planTotals.Count == 0) return new BalanceReportDto([], new BalanceTotalsDto(0, 0, 0));

        var ids = planTotals.Select(x => x.PatientId).ToList();

        var paymentTotals = await db.Payments.AsNoTracking()
            .Where(x => x.PaymentPlan.Status != PaymentPlanStatus.Cancelled && ids.Contains(x.PaymentPlan.PatientId))
            .GroupBy(x => x.PaymentPlan.PatientId)
            .Select(g => new { PatientId = g.Key, Paid = g.Sum(x => x.Amount), Last = g.Max(x => x.PaidAt) })
            .ToDictionaryAsync(x => x.PatientId, ct);

        var patients = await db.Patients.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, Name = p.FirstName + " " + p.LastName, p.DocumentId })
            .ToDictionaryAsync(p => p.Id, ct);

        var rows = planTotals
            .Where(t => patients.ContainsKey(t.PatientId))
            .Select(t =>
            {
                paymentTotals.TryGetValue(t.PatientId, out var pay);
                var paid = t.Down + (pay?.Paid ?? 0m);
                var patient = patients[t.PatientId];
                return new BalanceRowDto(t.PatientId, patient.Name, patient.DocumentId, t.ActivePlans,
                    t.Total, paid, t.Total - paid, pay?.Last);
            })
            .Where(r => !onlyWithBalance || r.Balance > 0)
            .OrderByDescending(r => r.Balance).ThenBy(r => r.PatientName)
            .ToList();

        var totals = new BalanceTotalsDto(rows.Sum(r => r.TotalAmount), rows.Sum(r => r.PaidAmount), rows.Sum(r => r.Balance));
        return new BalanceReportDto(rows, totals);
    }

    /// <summary>"Today" is the current UTC calendar day.</summary>
    public async Task<DashboardSummaryDto> DashboardAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var dayStart = now.Date;
        var dayEnd = dayStart.AddDays(1);

        var patients = await db.Patients.CountAsync(ct);
        var doctorsOnShift = await db.Shifts
            .Where(s => s.StartTime <= now && s.EndTime > now)
            .Select(s => s.DoctorId).Distinct().CountAsync(ct);
        var shiftsToday = await db.Shifts.CountAsync(s => s.StartTime < dayEnd && s.EndTime > dayStart, ct);

        var active = db.PaymentPlans.Where(p => p.Status == PaymentPlanStatus.Active);
        var activePlans = await active.CountAsync(ct);
        var financed = await active.SumAsync(p => (decimal?)(p.TotalAmount - p.DownPayment), ct) ?? 0m;
        var paid = await db.Payments.Where(x => x.PaymentPlan.Status == PaymentPlanStatus.Active)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;

        var lowStock = await db.Medications.CountAsync(MedicationDto.LowStock, ct);

        var alerts = await db.VitalSigns.AsNoTracking()
            .Where(VitalSignService.IsAbnormal)
            .OrderByDescending(v => v.RecordedAt).ThenByDescending(v => v.Id)
            .Take(RecentAlertCount)
            .Select(v => new VitalSignAlertDto
            {
                Id = v.Id,
                PatientId = v.PatientId,
                PatientName = v.Patient.FirstName + " " + v.Patient.LastName,
                RecordedAt = v.RecordedAt,
                Systolic = v.Systolic,
                Diastolic = v.Diastolic,
                Glucose = v.Glucose,
                OxygenSaturation = v.OxygenSaturation,
                HeartRate = v.HeartRate,
                Temperature = v.Temperature,
                Notes = v.Notes,
                RecordedBy = v.CreatedBy,
            })
            .ToListAsync(ct);

        return new DashboardSummaryDto(patients, doctorsOnShift, shiftsToday, activePlans, financed - paid, lowStock, alerts);
    }
}
