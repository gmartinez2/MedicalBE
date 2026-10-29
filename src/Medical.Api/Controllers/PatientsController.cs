using Medical.Api.Security;
using Medical.Application.Common;
using Medical.Application.Patients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Medical.Api.Controllers;

[ApiController]
[Route("api/patients")]
[Produces("application/json")]
public sealed class PatientsController(IPatientService patients) : ControllerBase
{
    /// <summary>Lists patients; <c>search</c> matches first/last name and document id.</summary>
    [HttpGet]
    [Authorize(Policy = Policies.AnyStaff)]
    public Task<PagedResult<PatientDto>> List([FromQuery] PageQuery query, CancellationToken ct) => patients.ListAsync(query, ct);

    /// <summary>Gets a patient by id.</summary>
    [HttpGet("{id:int}")]
    [Authorize(Policy = Policies.AnyStaff)]
    [ProducesResponseType(typeof(PatientDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<PatientDto> Get(int id, CancellationToken ct) => patients.GetAsync(id, ct);

    /// <summary>Creates a patient.</summary>
    /// <response code="409">Another patient already has this document id.</response>
    [HttpPost]
    [Authorize(Policy = Policies.ClinicalStaff)]
    [ProducesResponseType(typeof(PatientDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PatientDto>> Create(PatientInput input, CancellationToken ct)
    {
        var dto = await patients.CreateAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    /// <summary>Updates a patient.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.ClinicalStaff)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<PatientDto> Update(int id, PatientInput input, CancellationToken ct) => patients.UpdateAsync(id, input, ct);

    /// <summary>Soft-deletes a patient.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.ClinicalStaff)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await patients.DeleteAsync(id, ct);
        return NoContent();
    }
}
