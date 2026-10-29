using Medical.Api.Security;
using Medical.Application.Billing;
using Medical.Application.Common;
using Medical.Application.Doctors;
using Medical.Application.Medications;
using Medical.Application.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Medical.Api.Controllers;

/// <summary>Doctors. Read: all roles · Write: Admin.</summary>
[ApiController]
[Route("api/doctors")]
[Produces("application/json")]
public sealed class DoctorsController(IDoctorService doctors) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.AnyStaff)]
    public Task<PagedResult<DoctorDto>> List([FromQuery] PageQuery query, CancellationToken ct) => doctors.ListAsync(query, ct);

    [HttpGet("{id:int}")]
    [Authorize(Policy = Policies.AnyStaff)]
    public Task<DoctorDto> Get(int id, CancellationToken ct) => doctors.GetAsync(id, ct);

    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    [ProducesResponseType(typeof(DoctorDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<DoctorDto>> Create(DoctorInput input, CancellationToken ct)
    {
        var dto = await doctors.CreateAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public Task<DoctorDto> Update(int id, DoctorInput input, CancellationToken ct) => doctors.UpdateAsync(id, input, ct);

    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await doctors.DeleteAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Doctor shifts. Read: all roles · Write: Admin. Overlapping shifts for a doctor → 409.</summary>
[ApiController]
[Route("api/shifts")]
[Produces("application/json")]
public sealed class ShiftsController(IShiftService shifts) : ControllerBase
{
    /// <summary>Lists shifts intersecting [from, to), optionally for one doctor.</summary>
    [HttpGet]
    [Authorize(Policy = Policies.AnyStaff)]
    public Task<IReadOnlyList<ShiftDto>> List([FromQuery] ShiftQuery query, CancellationToken ct) => shifts.ListAsync(query, ct);

    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    [ProducesResponseType(typeof(ShiftDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ShiftDto>> Create(ShiftInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await shifts.CreateAsync(input, ct));

    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.AdminOnly)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ShiftDto> Update(int id, ShiftInput input, CancellationToken ct) => shifts.UpdateAsync(id, input, ct);

    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await shifts.DeleteAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Payment plans and payments. Admin/Billing.</summary>
[ApiController]
[Produces("application/json")]
[Authorize(Policy = Policies.BillingStaff)]
public sealed class PaymentPlansController(IPaymentPlanService plans) : ControllerBase
{
    /// <summary>Lists plans; <c>search</c> matches patient name, document id or description.</summary>
    [HttpGet("api/payment-plans")]
    public Task<PagedResult<PaymentPlanDto>> List([FromQuery] PaymentPlanQuery query, CancellationToken ct) => plans.ListAsync(query, ct);

    /// <summary>Plan detail with computed installments and registered payments.</summary>
    [HttpGet("api/payment-plans/{id:int}")]
    public Task<PaymentPlanDetailDto> Get(int id, CancellationToken ct) => plans.GetAsync(id, ct);

    [HttpGet("api/patients/{patientId:int}/payment-plans")]
    public Task<IReadOnlyList<PaymentPlanDto>> ListByPatient(int patientId, CancellationToken ct) => plans.ListByPatientAsync(patientId, ct);

    [HttpPost("api/payment-plans")]
    [ProducesResponseType(typeof(PaymentPlanDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<PaymentPlanDto>> Create(PaymentPlanInput input, CancellationToken ct)
    {
        var dto = await plans.CreateAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    [HttpPut("api/payment-plans/{id:int}")]
    public Task<PaymentPlanDto> Update(int id, PaymentPlanInput input, CancellationToken ct) => plans.UpdateAsync(id, input, ct);

    [HttpDelete("api/payment-plans/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await plans.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Registers a payment; amount must not exceed the outstanding balance.</summary>
    [HttpPost("api/payment-plans/{id:int}/payments")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PaymentDto>> AddPayment(int id, PaymentInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await plans.AddPaymentAsync(id, input, ct));

    [HttpDelete("api/payments/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeletePayment(int id, CancellationToken ct)
    {
        await plans.DeletePaymentAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Medication inventory. Read: all roles · Write: Admin/Doctor.</summary>
[ApiController]
[Route("api/medications")]
[Produces("application/json")]
public sealed class MedicationsController(IMedicationService medications) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.AnyStaff)]
    public Task<PagedResult<MedicationDto>> List([FromQuery] PageQuery query, CancellationToken ct) => medications.ListAsync(query, ct);

    [HttpGet("{id:int}")]
    [Authorize(Policy = Policies.AnyStaff)]
    public Task<MedicationDto> Get(int id, CancellationToken ct) => medications.GetAsync(id, ct);

    [HttpPost]
    [Authorize(Policy = Policies.Physicians)]
    [ProducesResponseType(typeof(MedicationDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<MedicationDto>> Create(MedicationInput input, CancellationToken ct)
    {
        var dto = await medications.CreateAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.Physicians)]
    public Task<MedicationDto> Update(int id, MedicationInput input, CancellationToken ct) => medications.UpdateAsync(id, input, ct);

    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.Physicians)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await medications.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/reports")]
[Produces("application/json")]
public sealed class ReportsController(IReportService reports) : ControllerBase
{
    /// <summary>Full medical history for each patient whose full name contains <c>patientName</c> (case-insensitive, max 50 patients).</summary>
    [HttpGet("medical-history")]
    [Authorize(Policy = Policies.ClinicalStaff)]
    public Task<IReadOnlyList<MedicalHistoryReportDto>> MedicalHistory([FromQuery] string? patientName, CancellationToken ct) =>
        reports.MedicalHistoryAsync(patientName, ct);

    /// <summary>Outstanding balances per patient over non-cancelled plans.</summary>
    [HttpGet("balances")]
    [Authorize(Policy = Policies.BillingStaff)]
    public Task<BalanceReportDto> Balances([FromQuery] string? patientName, [FromQuery] bool onlyWithBalance = true, CancellationToken ct = default) =>
        reports.BalancesAsync(patientName, onlyWithBalance, ct);
}

[ApiController]
[Route("api/dashboard")]
[Produces("application/json")]
public sealed class DashboardController(IReportService reports) : ControllerBase
{
    /// <summary>Headline counters and the 10 most recent abnormal vital signs.</summary>
    [HttpGet("summary")]
    [Authorize(Policy = Policies.AnyStaff)]
    public Task<DashboardSummaryDto> Summary(CancellationToken ct) => reports.DashboardAsync(ct);
}
