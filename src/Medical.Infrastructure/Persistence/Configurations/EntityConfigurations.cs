using Medical.Domain.Entities;
using Medical.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Medical.Infrastructure.Persistence.Configurations;

// Delete-behavior rules (SQL Server rejects multiple cascade paths):
//  * Patient → children: NO ACTION. Patients are soft-deleted, never physically removed.
//  * Doctor → records/diagnoses/treatments: SET NULL (history survives); Doctor → shifts: CASCADE.
//  * Diagnosis/Medication → treatments: NO ACTION in DB; services null the FK before deleting.
//  * PaymentPlan → payments: CASCADE.
// Children of a patient share the patient's soft-delete filter so a deleted patient's data disappears everywhere.

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        b.HasIndex(u => u.FullName);
    }
}

internal sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> b)
    {
        b.HasQueryFilter(p => !p.IsDeleted);
        b.Property(p => p.FirstName).HasMaxLength(100).IsRequired();
        b.Property(p => p.LastName).HasMaxLength(100).IsRequired();
        b.Property(p => p.DocumentId).HasMaxLength(50).IsRequired();
        b.Property(p => p.Phone).HasMaxLength(30);
        b.Property(p => p.Email).HasMaxLength(256);
        b.Property(p => p.Address).HasMaxLength(300);
        b.Property(p => p.BloodType).HasMaxLength(10);
        b.Property(p => p.Allergies).HasMaxLength(1000);

        b.HasIndex(p => p.DocumentId).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(p => new { p.LastName, p.FirstName });
        b.HasIndex(p => p.FirstName);
    }
}

internal sealed class DoctorConfiguration : IEntityTypeConfiguration<Doctor>
{
    public void Configure(EntityTypeBuilder<Doctor> b)
    {
        b.Property(d => d.FirstName).HasMaxLength(100).IsRequired();
        b.Property(d => d.LastName).HasMaxLength(100).IsRequired();
        b.Property(d => d.Specialty).HasMaxLength(100).IsRequired();
        b.Property(d => d.LicenseNumber).HasMaxLength(50).IsRequired();
        b.Property(d => d.Phone).HasMaxLength(30);
        b.Property(d => d.Email).HasMaxLength(256);

        b.HasIndex(d => d.LicenseNumber).IsUnique();
        b.HasIndex(d => new { d.LastName, d.FirstName });
    }
}

internal sealed class MedicalRecordConfiguration : IEntityTypeConfiguration<MedicalRecord>
{
    public void Configure(EntityTypeBuilder<MedicalRecord> b)
    {
        b.HasQueryFilter(r => !r.Patient.IsDeleted);
        b.Property(r => r.Reason).HasMaxLength(500).IsRequired();
        b.Property(r => r.Notes).HasMaxLength(4000);
        b.HasOne(r => r.Patient).WithMany(p => p.Records).HasForeignKey(r => r.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(r => r.Doctor).WithMany().HasForeignKey(r => r.DoctorId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(r => new { r.PatientId, r.VisitDate });
    }
}

internal sealed class DiagnosisConfiguration : IEntityTypeConfiguration<Diagnosis>
{
    public void Configure(EntityTypeBuilder<Diagnosis> b)
    {
        b.HasQueryFilter(d => !d.Patient.IsDeleted);
        b.Property(d => d.Code).HasMaxLength(20);
        b.Property(d => d.Description).HasMaxLength(1000).IsRequired();
        b.Property(d => d.Notes).HasMaxLength(4000);
        b.HasOne(d => d.Patient).WithMany(p => p.Diagnoses).HasForeignKey(d => d.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(d => d.Doctor).WithMany().HasForeignKey(d => d.DoctorId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(d => new { d.PatientId, d.DiagnosedAt });
    }
}

internal sealed class TreatmentConfiguration : IEntityTypeConfiguration<Treatment>
{
    public void Configure(EntityTypeBuilder<Treatment> b)
    {
        b.HasQueryFilter(t => !t.Patient.IsDeleted);
        b.Property(t => t.Description).HasMaxLength(1000).IsRequired();
        b.Property(t => t.Instructions).HasMaxLength(2000);
        b.HasOne(t => t.Patient).WithMany(p => p.Treatments).HasForeignKey(t => t.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(t => t.Doctor).WithMany().HasForeignKey(t => t.DoctorId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(t => t.Diagnosis).WithMany().HasForeignKey(t => t.DiagnosisId).OnDelete(DeleteBehavior.ClientSetNull);
        b.HasOne(t => t.Medication).WithMany().HasForeignKey(t => t.MedicationId).OnDelete(DeleteBehavior.ClientSetNull);
        b.HasIndex(t => new { t.PatientId, t.StartDate });
    }
}

internal sealed class MedicalImageConfiguration : IEntityTypeConfiguration<MedicalImage>
{
    public void Configure(EntityTypeBuilder<MedicalImage> b)
    {
        b.HasQueryFilter(i => !i.Patient.IsDeleted);
        b.Property(i => i.Title).HasMaxLength(200).IsRequired();
        b.Property(i => i.StudyType).HasMaxLength(100).IsRequired();
        b.Property(i => i.Description).HasMaxLength(1000);
        b.Property(i => i.FileName).HasMaxLength(255).IsRequired();
        b.Property(i => i.StoragePath).HasMaxLength(100).IsRequired();
        b.Property(i => i.ContentType).HasMaxLength(100).IsRequired();
        b.HasOne(i => i.Patient).WithMany(p => p.Images).HasForeignKey(i => i.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(i => new { i.PatientId, i.StudyDate });
    }
}

internal sealed class VitalSignConfiguration : IEntityTypeConfiguration<VitalSign>
{
    public void Configure(EntityTypeBuilder<VitalSign> b)
    {
        b.HasQueryFilter(v => !v.Patient.IsDeleted);
        b.Property(v => v.Glucose).HasPrecision(5, 1);
        b.Property(v => v.Temperature).HasPrecision(5, 1);
        b.Property(v => v.Notes).HasMaxLength(1000);
        b.HasOne(v => v.Patient).WithMany(p => p.VitalSigns).HasForeignKey(v => v.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(v => new { v.PatientId, v.RecordedAt });
        b.HasIndex(v => v.RecordedAt);
    }
}

internal sealed class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> b)
    {
        b.Property(s => s.Location).HasMaxLength(200).IsRequired();
        b.Property(s => s.Notes).HasMaxLength(1000);
        b.HasOne(s => s.Doctor).WithMany(d => d.Shifts).HasForeignKey(s => s.DoctorId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(s => new { s.DoctorId, s.StartTime }).IncludeProperties(s => s.EndTime);
        b.HasIndex(s => new { s.StartTime, s.EndTime });
    }
}

internal sealed class MedicationConfiguration : IEntityTypeConfiguration<Medication>
{
    public void Configure(EntityTypeBuilder<Medication> b)
    {
        b.Property(m => m.CompoundName).HasMaxLength(200).IsRequired();
        b.Property(m => m.Dose).HasMaxLength(100).IsRequired();
        b.Property(m => m.PharmaceuticalCompany).HasMaxLength(200).IsRequired();
        b.Property(m => m.Presentation).HasMaxLength(200);
        b.HasIndex(m => m.CompoundName);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Medications_Stock", "[Stock] >= 0");
            t.HasCheckConstraint("CK_Medications_MinimumStock", "[MinimumStock] >= 0");
        });
    }
}

internal sealed class PaymentPlanConfiguration : IEntityTypeConfiguration<PaymentPlan>
{
    public void Configure(EntityTypeBuilder<PaymentPlan> b)
    {
        b.HasQueryFilter(p => !p.Patient.IsDeleted);
        b.Property(p => p.Description).HasMaxLength(500).IsRequired();
        b.Property(p => p.TotalAmount).HasPrecision(18, 2);
        b.Property(p => p.DownPayment).HasPrecision(18, 2);
        b.Property(p => p.Terms).HasMaxLength(4000);
        b.Property(p => p.RowVersion).IsRowVersion();
        b.HasOne(p => p.Patient).WithMany(p => p.PaymentPlans).HasForeignKey(p => p.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(p => p.Status);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_PaymentPlans_Amounts", "[TotalAmount] > 0 AND [DownPayment] >= 0 AND [DownPayment] <= [TotalAmount]");
            t.HasCheckConstraint("CK_PaymentPlans_Installments", "[NumberOfInstallments] BETWEEN 1 AND 120");
        });
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.HasQueryFilter(p => !p.PaymentPlan.Patient.IsDeleted);
        b.Property(p => p.Amount).HasPrecision(18, 2);
        b.Property(p => p.Reference).HasMaxLength(100);
        b.HasOne(p => p.PaymentPlan).WithMany(p => p.Payments).HasForeignKey(p => p.PaymentPlanId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t => t.HasCheckConstraint("CK_Payments_Amount", "[Amount] > 0"));
    }
}
