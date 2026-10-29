using Medical.Domain.Common;
using Medical.Domain.Enums;

namespace Medical.Domain.Entities;

public class Patient : AuditableEntity
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

    /// <summary>Soft-delete flag; deleted patients are hidden by a global query filter.</summary>
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public ICollection<MedicalRecord> Records { get; set; } = [];
    public ICollection<Diagnosis> Diagnoses { get; set; } = [];
    public ICollection<Treatment> Treatments { get; set; } = [];
    public ICollection<VitalSign> VitalSigns { get; set; } = [];
    public ICollection<MedicalImage> Images { get; set; } = [];
    public ICollection<PaymentPlan> PaymentPlans { get; set; } = [];
}

public class Doctor : AuditableEntity
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Specialty { get; set; } = string.Empty;
    public string LicenseNumber { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }

    public ICollection<Shift> Shifts { get; set; } = [];
}

public class MedicalRecord : AuditableEntity
{
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public DateTime VisitDate { get; set; }
    public int? DoctorId { get; set; }
    public Doctor? Doctor { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class Diagnosis : AuditableEntity
{
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public string? Code { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateOnly DiagnosedAt { get; set; }
    public DiagnosisStatus Status { get; set; }
    public int? DoctorId { get; set; }
    public Doctor? Doctor { get; set; }
    public string? Notes { get; set; }
}

public class Treatment : AuditableEntity
{
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    public int? DiagnosisId { get; set; }
    public Diagnosis? Diagnosis { get; set; }
    public int? MedicationId { get; set; }
    public Medication? Medication { get; set; }
    public int? DoctorId { get; set; }
    public Doctor? Doctor { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Instructions { get; set; }
    public TreatmentStatus Status { get; set; }
}

public class MedicalImage : AuditableEntity
{
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string StudyType { get; set; } = string.Empty;
    public DateOnly StudyDate { get; set; }
    public string? Description { get; set; }

    /// <summary>Original (sanitized) file name supplied by the uploader.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Path relative to the storage root (GUID + extension).</summary>
    public string StoragePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

public class VitalSign : AuditableEntity
{
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public DateTime RecordedAt { get; set; }
    public int Systolic { get; set; }
    public int Diastolic { get; set; }
    public decimal Glucose { get; set; }
    public int OxygenSaturation { get; set; }
    public int? HeartRate { get; set; }
    public decimal? Temperature { get; set; }
    public string? Notes { get; set; }
}

public class Shift : AuditableEntity
{
    public int DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string Location { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class Medication : AuditableEntity
{
    public string CompoundName { get; set; } = string.Empty;
    public string Dose { get; set; } = string.Empty;
    public string PharmaceuticalCompany { get; set; } = string.Empty;
    public string? Presentation { get; set; }
    public int Stock { get; set; }
    public int MinimumStock { get; set; }
}

public class PaymentPlan : AuditableEntity
{
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal DownPayment { get; set; }
    public int NumberOfInstallments { get; set; }
    public PaymentFrequency Frequency { get; set; }
    public DateOnly StartDate { get; set; }
    public string? Terms { get; set; }
    public PaymentPlanStatus Status { get; set; }

    /// <summary>Optimistic concurrency token; guards balance checks against concurrent payments.</summary>
    public byte[] RowVersion { get; set; } = [];

    public ICollection<Payment> Payments { get; set; } = [];
}

public class Payment : AuditableEntity
{
    public int PaymentPlanId { get; set; }
    public PaymentPlan PaymentPlan { get; set; } = null!;
    public decimal Amount { get; set; }
    public DateOnly PaidAt { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
}
