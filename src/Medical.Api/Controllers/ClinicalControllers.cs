using Medical.Api.Security;
using Medical.Application.Clinical;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Medical.Api.Controllers;

/// <summary>Medical records (visits). Read: Admin/Doctor/Nurse · Write: Admin/Doctor.</summary>
[ApiController]
[Produces("application/json")]
public sealed class MedicalRecordsController(IMedicalRecordService records) : ControllerBase
{
    [HttpGet("api/patients/{patientId:int}/records")]
    [Authorize(Policy = Policies.ClinicalStaff)]
    public Task<IReadOnlyList<MedicalRecordDto>> List(int patientId, CancellationToken ct) => records.ListByPatientAsync(patientId, ct);

    [HttpPost("api/patients/{patientId:int}/records")]
    [Authorize(Policy = Policies.Physicians)]
    [ProducesResponseType(typeof(MedicalRecordDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<MedicalRecordDto>> Create(int patientId, MedicalRecordInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await records.CreateAsync(patientId, input, ct));

    [HttpPut("api/records/{id:int}")]
    [Authorize(Policy = Policies.Physicians)]
    public Task<MedicalRecordDto> Update(int id, MedicalRecordInput input, CancellationToken ct) => records.UpdateAsync(id, input, ct);

    [HttpDelete("api/records/{id:int}")]
    [Authorize(Policy = Policies.Physicians)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await records.DeleteAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Diagnoses. Read: Admin/Doctor/Nurse · Write: Admin/Doctor.</summary>
[ApiController]
[Produces("application/json")]
public sealed class DiagnosesController(IDiagnosisService diagnoses) : ControllerBase
{
    [HttpGet("api/patients/{patientId:int}/diagnoses")]
    [Authorize(Policy = Policies.ClinicalStaff)]
    public Task<IReadOnlyList<DiagnosisDto>> List(int patientId, CancellationToken ct) => diagnoses.ListByPatientAsync(patientId, ct);

    [HttpPost("api/patients/{patientId:int}/diagnoses")]
    [Authorize(Policy = Policies.Physicians)]
    [ProducesResponseType(typeof(DiagnosisDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<DiagnosisDto>> Create(int patientId, DiagnosisInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await diagnoses.CreateAsync(patientId, input, ct));

    [HttpPut("api/diagnoses/{id:int}")]
    [Authorize(Policy = Policies.Physicians)]
    public Task<DiagnosisDto> Update(int id, DiagnosisInput input, CancellationToken ct) => diagnoses.UpdateAsync(id, input, ct);

    [HttpDelete("api/diagnoses/{id:int}")]
    [Authorize(Policy = Policies.Physicians)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await diagnoses.DeleteAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Treatments. Read: Admin/Doctor/Nurse · Write: Admin/Doctor.</summary>
[ApiController]
[Produces("application/json")]
public sealed class TreatmentsController(ITreatmentService treatments) : ControllerBase
{
    [HttpGet("api/patients/{patientId:int}/treatments")]
    [Authorize(Policy = Policies.ClinicalStaff)]
    public Task<IReadOnlyList<TreatmentDto>> List(int patientId, CancellationToken ct) => treatments.ListByPatientAsync(patientId, ct);

    [HttpPost("api/patients/{patientId:int}/treatments")]
    [Authorize(Policy = Policies.Physicians)]
    [ProducesResponseType(typeof(TreatmentDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<TreatmentDto>> Create(int patientId, TreatmentInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await treatments.CreateAsync(patientId, input, ct));

    [HttpPut("api/treatments/{id:int}")]
    [Authorize(Policy = Policies.Physicians)]
    public Task<TreatmentDto> Update(int id, TreatmentInput input, CancellationToken ct) => treatments.UpdateAsync(id, input, ct);

    [HttpDelete("api/treatments/{id:int}")]
    [Authorize(Policy = Policies.Physicians)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await treatments.DeleteAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Vital signs with server-computed clinical flags. Admin/Doctor/Nurse.</summary>
[ApiController]
[Produces("application/json")]
[Authorize(Policy = Policies.ClinicalStaff)]
public sealed class VitalSignsController(IVitalSignService vitals) : ControllerBase
{
    /// <summary>Lists vitals ordered by recordedAt desc, optionally within [from, to].</summary>
    [HttpGet("api/patients/{patientId:int}/vitals")]
    public Task<IReadOnlyList<VitalSignDto>> List(int patientId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct) =>
        vitals.ListByPatientAsync(patientId, from, to, ct);

    [HttpPost("api/patients/{patientId:int}/vitals")]
    [ProducesResponseType(typeof(VitalSignDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<VitalSignDto>> Create(int patientId, VitalSignInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await vitals.CreateAsync(patientId, input, ct));

    [HttpDelete("api/vitals/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await vitals.DeleteAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Multipart form for image uploads.</summary>
public sealed class ImageUploadForm
{
    public IFormFile? File { get; set; }
    public string? Title { get; set; }
    public string? StudyType { get; set; }
    public DateOnly? StudyDate { get; set; }
    public string? Description { get; set; }
}

/// <summary>Medical images (jpg/jpeg/png/webp/pdf, ≤ 10 MB). Admin/Doctor/Nurse.</summary>
[ApiController]
[Produces("application/json")]
[Authorize(Policy = Policies.ClinicalStaff)]
public sealed class ImagesController(IMedicalImageService images) : ControllerBase
{
    private const long RequestLimit = 11 * 1024 * 1024; // file limit + multipart overhead

    [HttpGet("api/patients/{patientId:int}/images")]
    public Task<IReadOnlyList<MedicalImageDto>> List(int patientId, CancellationToken ct) => images.ListByPatientAsync(patientId, ct);

    /// <summary>Uploads an image or PDF for a patient (multipart/form-data).</summary>
    [HttpPost("api/patients/{patientId:int}/images")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(RequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = RequestLimit)]
    [ProducesResponseType(typeof(MedicalImageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MedicalImageDto>> Upload(int patientId, [FromForm] ImageUploadForm form, CancellationToken ct)
    {
        if (form.File is null)
            throw new Application.Common.BusinessValidationException("file", "Debe adjuntar un archivo.");

        await using var stream = form.File.OpenReadStream();
        var dto = await images.UploadAsync(patientId, new ImageUpload
        {
            Content = stream,
            FileName = form.File.FileName,
            ContentType = form.File.ContentType ?? string.Empty,
            Length = form.File.Length,
            Title = form.Title ?? string.Empty,
            StudyType = form.StudyType ?? string.Empty,
            StudyDate = form.StudyDate ?? default,
            Description = form.Description,
        }, ct);
        return StatusCode(StatusCodes.Status201Created, dto);
    }

    /// <summary>Streams the stored file with its content type (fetch it as a blob with the Bearer header).</summary>
    [HttpGet("api/images/{id:int}/file")]
    [Produces("application/octet-stream")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Download(int id, CancellationToken ct)
    {
        var file = await images.OpenAsync(id, ct);
        Response.Headers.CacheControl = "private, no-store";
        return File(file.Content, file.ContentType, enableRangeProcessing: true);
    }

    [HttpDelete("api/images/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await images.DeleteAsync(id, ct);
        return NoContent();
    }
}
