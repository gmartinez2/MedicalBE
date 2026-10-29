using System.Globalization;
using FluentValidation;
using Medical.Application.Billing;
using Medical.Application.Clinical;
using Medical.Application.Doctors;
using Medical.Application.Medications;
using Medical.Application.Patients;
using Medical.Application.Reports;
using Microsoft.Extensions.DependencyInjection;

namespace Medical.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // User-facing validation messages are Spanish (the UI shows them verbatim).
        ValidatorOptions.Global.LanguageManager.Culture = new CultureInfo("es");

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, ServiceLifetime.Scoped, includeInternalTypes: true);
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<IPatientService, PatientService>();
        services.AddScoped<IDoctorService, DoctorService>();
        services.AddScoped<IShiftService, ShiftService>();
        services.AddScoped<IMedicalRecordService, MedicalRecordService>();
        services.AddScoped<IDiagnosisService, DiagnosisService>();
        services.AddScoped<ITreatmentService, TreatmentService>();
        services.AddScoped<IVitalSignService, VitalSignService>();
        services.AddScoped<IMedicalImageService, MedicalImageService>();
        services.AddScoped<IMedicationService, MedicationService>();
        services.AddScoped<IPaymentPlanService, PaymentPlanService>();
        services.AddScoped<IReportService, ReportService>();
        return services;
    }
}
