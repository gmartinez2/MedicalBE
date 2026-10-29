using System.Linq.Expressions;
using FluentValidation;
using Medical.Application.Common;
using Medical.Application.Patients;
using Medical.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Medical.Application.Clinical;

/// <summary>Transport-agnostic upload payload; the API layer maps <c>IFormFile</c> onto it.</summary>
public sealed class ImageUpload
{
    public required Stream Content { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long Length { get; init; }
    public string Title { get; set; } = string.Empty;
    public string StudyType { get; set; } = string.Empty;
    public DateOnly StudyDate { get; set; }
    public string? Description { get; set; }
}

public class MedicalImageDto
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string StudyType { get; set; } = string.Empty;
    public DateOnly StudyDate { get; set; }
    public string? Description { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }
    public string? UploadedBy { get; set; }

    public static readonly Expression<Func<MedicalImage, MedicalImageDto>> Projection = i => new MedicalImageDto
    {
        Id = i.Id,
        PatientId = i.PatientId,
        Title = i.Title,
        StudyType = i.StudyType,
        StudyDate = i.StudyDate,
        Description = i.Description,
        FileName = i.FileName,
        ContentType = i.ContentType,
        SizeBytes = i.SizeBytes,
        UploadedAt = i.CreatedAt,
        UploadedBy = i.CreatedBy,
    };
}

public sealed record StoredFile(Stream Content, string ContentType, string FileName);

public static class ImageFileRules
{
    public const long MaxSizeBytes = 10 * 1024 * 1024;

    /// <summary>Allowed extension → canonical content type.</summary>
    public static readonly IReadOnlyDictionary<string, string> AllowedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".pdf"] = "application/pdf",
    };

    /// <summary>Checks the file signature ("magic bytes") matches the declared type.</summary>
    public static bool HasValidSignature(ReadOnlySpan<byte> header, string contentType) => contentType switch
    {
        "image/jpeg" => header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
        "image/png" => header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        "image/webp" => header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8),
        "application/pdf" => header.Length >= 5 && header[..5].SequenceEqual("%PDF-"u8),
        _ => false,
    };
}

public sealed class ImageUploadValidator : AbstractValidator<ImageUpload>
{
    public ImageUploadValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200).WithName("Título");
        RuleFor(x => x.StudyType).NotEmpty().MaximumLength(100).WithName("Tipo de estudio");
        RuleFor(x => x.StudyDate).NotEmpty().WithName("Fecha del estudio");
        RuleFor(x => x.Description).MaximumLength(1000).WithName("Descripción");
        RuleFor(x => x.Length)
            .GreaterThan(0).WithMessage("El archivo está vacío.")
            .LessThanOrEqualTo(ImageFileRules.MaxSizeBytes).WithMessage("El archivo no puede superar los 10 MB.")
            .OverridePropertyName("file");
        RuleFor(x => x.FileName)
            .Must(n => ImageFileRules.AllowedTypes.ContainsKey(Path.GetExtension(n)))
            .WithMessage("Tipo de archivo no permitido. Formatos aceptados: jpg, jpeg, png, webp, pdf.")
            .OverridePropertyName("file");
        RuleFor(x => x)
            .Must(x => ImageFileRules.AllowedTypes.TryGetValue(Path.GetExtension(x.FileName), out var expected) &&
                       string.Equals(expected, NormalizeContentType(x.ContentType), StringComparison.OrdinalIgnoreCase))
            .When(x => ImageFileRules.AllowedTypes.ContainsKey(Path.GetExtension(x.FileName)))
            .WithMessage("El tipo de contenido del archivo no coincide con su extensión.")
            .OverridePropertyName("file");
    }

    // Browsers occasionally send image/jpg; treat it as image/jpeg.
    private static string NormalizeContentType(string contentType) =>
        contentType.Split(';')[0].Trim().ToLowerInvariant() switch
        {
            "image/jpg" or "image/pjpeg" => "image/jpeg",
            var other => other,
        };
}

public interface IMedicalImageService
{
    Task<IReadOnlyList<MedicalImageDto>> ListByPatientAsync(int patientId, CancellationToken ct);
    Task<MedicalImageDto> UploadAsync(int patientId, ImageUpload upload, CancellationToken ct);
    Task<StoredFile> OpenAsync(int id, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}

public sealed class MedicalImageService(IAppDbContext db, IFileStorage storage, IValidator<ImageUpload> validator) : IMedicalImageService
{
    private const string NotFoundMessage = "Imagen no encontrada.";

    public async Task<IReadOnlyList<MedicalImageDto>> ListByPatientAsync(int patientId, CancellationToken ct)
    {
        await PatientService.EnsureExistsAsync(db, patientId, ct);
        return await db.MedicalImages.AsNoTracking()
            .Where(i => i.PatientId == patientId)
            .OrderByDescending(i => i.StudyDate).ThenByDescending(i => i.Id)
            .Select(MedicalImageDto.Projection)
            .ToListAsync(ct);
    }

    public async Task<MedicalImageDto> UploadAsync(int patientId, ImageUpload upload, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(upload, ct);
        await PatientService.EnsureExistsAsync(db, patientId, ct);

        var extension = Path.GetExtension(upload.FileName).ToLowerInvariant();
        var contentType = ImageFileRules.AllowedTypes[extension];

        // Buffer the (≤10 MB) upload so the signature can be checked before anything touches disk.
        using var buffer = new MemoryStream(capacity: (int)Math.Min(upload.Length, ImageFileRules.MaxSizeBytes));
        await upload.Content.CopyToAsync(buffer, ct);
        if (buffer.Length > ImageFileRules.MaxSizeBytes)
            throw new BusinessValidationException("file", "El archivo no puede superar los 10 MB.");
        if (!ImageFileRules.HasValidSignature(buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 16)), contentType))
            throw new BusinessValidationException("file", "El contenido del archivo no corresponde a un formato permitido.");

        buffer.Position = 0;
        var storagePath = await storage.SaveAsync(buffer, extension, ct);

        var image = new MedicalImage
        {
            PatientId = patientId,
            Title = upload.Title.Trim(),
            StudyType = upload.StudyType.Trim(),
            StudyDate = upload.StudyDate,
            Description = upload.Description.NullIfBlank(),
            FileName = SanitizeFileName(upload.FileName),
            StoragePath = storagePath,
            ContentType = contentType,
            SizeBytes = buffer.Length,
        };
        db.MedicalImages.Add(image);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            storage.Delete(storagePath); // do not leave orphaned files behind
            throw;
        }

        return await db.MedicalImages.AsNoTracking().Where(i => i.Id == image.Id).Select(MedicalImageDto.Projection).FirstAsync(ct);
    }

    public async Task<StoredFile> OpenAsync(int id, CancellationToken ct)
    {
        var meta = await db.MedicalImages.AsNoTracking()
                       .Where(i => i.Id == id)
                       .Select(i => new { i.StoragePath, i.ContentType, i.FileName })
                       .FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(NotFoundMessage);
        var stream = storage.OpenRead(meta.StoragePath)
                     ?? throw new NotFoundException("El archivo de la imagen no está disponible.");
        return new StoredFile(stream, meta.ContentType, meta.FileName);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var path = await db.MedicalImages.Where(i => i.Id == id).Select(i => i.StoragePath).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(NotFoundMessage);
        await db.MedicalImages.Where(i => i.Id == id).ExecuteDeleteAsync(ct);
        storage.Delete(path);
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "archivo";
        return cleaned.Length > 255 ? cleaned[^255..] : cleaned;
    }
}
