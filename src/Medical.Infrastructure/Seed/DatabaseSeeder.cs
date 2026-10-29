using Medical.Application.Users;
using Medical.Domain.Entities;
using Medical.Domain.Enums;
using Medical.Infrastructure.Identity;
using Medical.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Medical.Infrastructure.Seed;

/// <summary>
/// Idempotent seeding. Roles and the admin account are ensured in every environment;
/// demo users and clinical/billing sample data only in Development.
/// </summary>
public static class DatabaseSeeder
{
    public const string AdminEmail = "admin@hospital.local";
    private const string AdminPassword = "Admin123!";

    public static async Task SeedAsync(IServiceProvider services, bool includeDemoData, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseSeeder));
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var db = sp.GetRequiredService<ApplicationDbContext>();

        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        await EnsureUserAsync(userManager, logger, AdminEmail, "Administrador del Sistema", AdminPassword, Roles.Admin);

        if (!includeDemoData) return;

        await EnsureUserAsync(userManager, logger, "doctor@hospital.local", "Dra. Lucía Fernández", "Doctor123!", Roles.Doctor);
        await EnsureUserAsync(userManager, logger, "nurse@hospital.local", "Enf. Marta Herrera", "Nurse123!", Roles.Nurse);
        await EnsureUserAsync(userManager, logger, "billing@hospital.local", "Lic. Ricardo Paz", "Billing123!", Roles.Billing);

        if (await db.Doctors.AnyAsync(ct))
        {
            logger.LogInformation("Demo data already present; skipping clinical seed");
            return;
        }

        await SeedDemoDataAsync(db, ct);
        logger.LogInformation("Demo data seeded");
    }

    private static async Task EnsureUserAsync(
        UserManager<ApplicationUser> users, ILogger logger, string email, string fullName, string password, string role)
    {
        if (await users.FindByEmailAsync(email) is not null) return;

        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, FullName = fullName, IsActive = true };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Could not seed user {email}: {string.Join("; ", result.Errors.Select(e => e.Code))}");
        await users.AddToRoleAsync(user, role);
        logger.LogInformation("Seeded user {Email} with role {Role}", email, role);
    }

    private static async Task SeedDemoDataAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        var doctors = new[]
        {
            new Doctor { FirstName = "Carlos", LastName = "Mendoza", Specialty = "Cardiología", LicenseNumber = "COL-10234", Phone = "+504 2234-1001", Email = "cmendoza@hospital.local" },
            new Doctor { FirstName = "Lucía", LastName = "Fernández", Specialty = "Pediatría", LicenseNumber = "COL-10567", Phone = "+504 2234-1002", Email = "lfernandez@hospital.local" },
            new Doctor { FirstName = "Andrés", LastName = "Morales", Specialty = "Medicina Interna", LicenseNumber = "COL-11022", Phone = "+504 2234-1003", Email = "amorales@hospital.local" },
            new Doctor { FirstName = "Valeria", LastName = "Castillo", Specialty = "Endocrinología", LicenseNumber = "COL-11458", Phone = "+504 2234-1004", Email = "vcastillo@hospital.local" },
            new Doctor { FirstName = "Jorge", LastName = "Ramírez", Specialty = "Neumología", LicenseNumber = "COL-11873", Phone = "+504 2234-1005", Email = "jramirez@hospital.local" },
        };
        db.Doctors.AddRange(doctors);

        var patients = new[]
        {
            P("María José", "López Hernández", "0801-1978-01234", new DateOnly(1978, 3, 14), Gender.Female, "O+", "Penicilina"),
            P("Juan Carlos", "Martínez Reyes", "0801-1965-04567", new DateOnly(1965, 11, 2), Gender.Male, "A+", null),
            P("Ana Gabriela", "Rodríguez Flores", "0501-1990-07891", new DateOnly(1990, 7, 21), Gender.Female, "B+", "Sulfas"),
            P("José Luis", "García Mejía", "0801-1952-00987", new DateOnly(1952, 1, 30), Gender.Male, "O-", null),
            P("Sofía", "Hernández Castro", "0301-2015-03344", new DateOnly(2015, 5, 9), Gender.Female, "A-", "Maní"),
            P("Luis Fernando", "Pérez Aguilar", "0801-1983-06612", new DateOnly(1983, 9, 17), Gender.Male, "AB+", null),
            P("Carmen Elena", "Sánchez Ortiz", "0101-1970-02255", new DateOnly(1970, 12, 5), Gender.Female, "O+", "Ácido acetilsalicílico"),
            P("Miguel Ángel", "Torres Díaz", "0801-1999-08876", new DateOnly(1999, 4, 26), Gender.Male, "B-", null),
            P("Daniela", "Cruz Vásquez", "1801-1987-05430", new DateOnly(1987, 8, 11), Gender.Female, "A+", null),
            P("Roberto", "Núñez Zelaya", "0801-1960-09912", new DateOnly(1960, 2, 18), Gender.Male, "O+", "Látex"),
        };
        db.Patients.AddRange(patients);

        var meds = new[]
        {
            M("Paracetamol", "500 mg", "Laboratorios Finlay", "Tabletas, caja x 100", 850, 200),
            M("Ibuprofeno", "400 mg", "Laboratorios Andifar", "Tabletas, caja x 50", 420, 100),
            M("Amoxicilina", "500 mg", "Farmacéutica Centroamericana", "Cápsulas, caja x 21", 35, 60),
            M("Metformina", "850 mg", "Laboratorios Finlay", "Tabletas, caja x 60", 310, 80),
            M("Losartán", "50 mg", "Laboratorios Vijosa", "Tabletas, caja x 30", 260, 80),
            M("Enalapril", "10 mg", "Laboratorios Andifar", "Tabletas, caja x 30", 190, 60),
            M("Salbutamol", "100 mcg/dosis", "Farmacéutica Centroamericana", "Inhalador 200 dosis", 12, 25),
            M("Omeprazol", "20 mg", "Laboratorios Vijosa", "Cápsulas, caja x 28", 540, 120),
            M("Insulina glargina", "100 UI/mL", "Biofarma", "Pluma prellenada 3 mL", 48, 20),
            M("Atorvastatina", "20 mg", "Laboratorios Finlay", "Tabletas, caja x 30", 275, 70),
        };
        db.Medications.AddRange(meds);

        await db.SaveChangesAsync(ct);

        // Clinical history for the first patients.
        var hta = Dx(patients[1], "I10", "Hipertensión arterial esencial", today.AddYears(-3), DiagnosisStatus.Chronic, doctors[0]);
        var dm2 = Dx(patients[3], "E11", "Diabetes mellitus tipo 2", today.AddYears(-5), DiagnosisStatus.Chronic, doctors[3]);
        var asma = Dx(patients[4], "J45", "Asma bronquial leve persistente", today.AddMonths(-8), DiagnosisStatus.Active, doctors[4]);
        var faringitis = Dx(patients[0], "J02.9", "Faringitis aguda", today.AddMonths(-2), DiagnosisStatus.Resolved, doctors[2]);
        var dislipidemia = Dx(patients[6], "E78.5", "Hiperlipidemia", today.AddYears(-1), DiagnosisStatus.Active, doctors[2]);
        db.Diagnoses.AddRange(hta, dm2, asma, faringitis, dislipidemia);

        db.MedicalRecords.AddRange(
            Rec(patients[0], now.AddDays(-60), doctors[2], "Dolor de garganta y fiebre", "Se indica antibiótico por 7 días."),
            Rec(patients[0], now.AddDays(-10), doctors[2], "Control general", "Paciente asintomática."),
            Rec(patients[1], now.AddDays(-30), doctors[0], "Control de presión arterial", "Ajuste de dosis de losartán."),
            Rec(patients[3], now.AddDays(-14), doctors[3], "Control de glucosa", "Glucemias elevadas en ayunas."),
            Rec(patients[4], now.AddDays(-5), doctors[4], "Crisis de broncoespasmo", "Buena respuesta a salbutamol."),
            Rec(patients[6], now.AddDays(-20), doctors[2], "Resultados de perfil lipídico", null),
            Rec(patients[8], now.AddDays(-3), doctors[1], "Chequeo prenatal", "Evolución normal."));

        db.Treatments.AddRange(
            new Treatment { Patient = patients[1], Diagnosis = hta, Medication = meds[4], Doctor = doctors[0], Description = "Antihipertensivo diario", StartDate = today.AddYears(-3), Instructions = "1 tableta cada 24 horas", Status = TreatmentStatus.InProgress },
            new Treatment { Patient = patients[3], Diagnosis = dm2, Medication = meds[3], Doctor = doctors[3], Description = "Control glucémico oral", StartDate = today.AddYears(-5), Instructions = "1 tableta con desayuno y cena", Status = TreatmentStatus.InProgress },
            new Treatment { Patient = patients[3], Diagnosis = dm2, Medication = meds[8], Doctor = doctors[3], Description = "Insulina basal", StartDate = today.AddMonths(-1), Instructions = "10 UI subcutánea por la noche", Status = TreatmentStatus.InProgress },
            new Treatment { Patient = patients[4], Diagnosis = asma, Medication = meds[6], Doctor = doctors[4], Description = "Rescate broncodilatador", StartDate = today.AddMonths(-8), Instructions = "2 inhalaciones cada 6 horas si hay síntomas", Status = TreatmentStatus.InProgress },
            new Treatment { Patient = patients[0], Diagnosis = faringitis, Medication = meds[2], Doctor = doctors[2], Description = "Antibioticoterapia", StartDate = today.AddMonths(-2), EndDate = today.AddMonths(-2).AddDays(7), Instructions = "1 cápsula cada 8 horas por 7 días", Status = TreatmentStatus.Completed },
            new Treatment { Patient = patients[6], Diagnosis = dislipidemia, Medication = meds[9], Doctor = doctors[2], Description = "Estatina", StartDate = today.AddMonths(-11), Instructions = "1 tableta por la noche", Status = TreatmentStatus.InProgress });

        // Vital signs: a mix of normal and abnormal readings over the last days.
        db.VitalSigns.AddRange(
            V(patients[0], now.AddDays(-10), 118, 76, 92m, 98, 72, 36.6m),
            V(patients[1], now.AddDays(-30), 152, 96, 105m, 97, 80, 36.8m),
            V(patients[1], now.AddHours(-6), 146, 92, 110m, 97, 78, 36.7m),
            V(patients[3], now.AddDays(-14), 128, 82, 245m, 96, 88, 36.9m),
            V(patients[3], now.AddHours(-3), 132, 84, 64m, 96, 104, 36.8m),
            V(patients[4], now.AddDays(-5), 110, 70, 90m, 89, 112, 37.4m),
            V(patients[4], now.AddHours(-12), 108, 68, 95m, 95, 90, 36.9m),
            V(patients[6], now.AddDays(-20), 124, 80, 99m, 98, 70, 36.5m),
            V(patients[7], now.AddHours(-2), 120, 78, 98m, 97, 96, 38.6m),
            V(patients[9], now.AddDays(-1), 86, 55, 88m, 94, 58, 36.2m));

        // Shifts for the current week (Monday 00:00 UTC onwards), three 8h rotations per day.
        var monday = now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7));
        int[] startHours = [7, 15, 23];
        string[] locations = ["Emergencias", "Consulta Externa", "Hospitalización", "Unidad de Cuidados Intensivos", "Pediatría"];
        for (var day = 0; day < 7; day++)
        {
            for (var i = 0; i < doctors.Length; i++)
            {
                var start = DateTime.SpecifyKind(monday.AddDays(day).AddHours(startHours[(i + day) % 3]), DateTimeKind.Utc);
                db.Shifts.Add(new Shift { Doctor = doctors[i], StartTime = start, EndTime = start.AddHours(8), Location = locations[i] });
            }
        }

        // Payment plans with payments.
        var plan1 = new PaymentPlan
        {
            Patient = patients[1], Description = "Cateterismo cardíaco diagnóstico", TotalAmount = 15000m, DownPayment = 3000m,
            NumberOfInstallments = 12, Frequency = PaymentFrequency.Monthly, StartDate = today.AddMonths(-2),
            Terms = "Pagos mensuales los días de vencimiento. Mora de 2% mensual sobre cuotas vencidas.", Status = PaymentPlanStatus.Active,
        };
        plan1.Payments.Add(new Payment { Amount = 1000m, PaidAt = today.AddMonths(-2), Method = PaymentMethod.Transfer, Reference = "TRF-558812" });
        plan1.Payments.Add(new Payment { Amount = 1000m, PaidAt = today.AddMonths(-1), Method = PaymentMethod.Card, Reference = "POS-993104" });

        var plan2 = new PaymentPlan
        {
            Patient = patients[3], Description = "Hospitalización por descompensación diabética", TotalAmount = 4800m, DownPayment = 800m,
            NumberOfInstallments = 8, Frequency = PaymentFrequency.Biweekly, StartDate = today.AddDays(-42),
            Terms = "Cuotas quincenales.", Status = PaymentPlanStatus.Active,
        };
        plan2.Payments.Add(new Payment { Amount = 500m, PaidAt = today.AddDays(-42), Method = PaymentMethod.Cash });
        plan2.Payments.Add(new Payment { Amount = 500m, PaidAt = today.AddDays(-28), Method = PaymentMethod.Cash });
        plan2.Payments.Add(new Payment { Amount = 500m, PaidAt = today.AddDays(-14), Method = PaymentMethod.Transfer, Reference = "TRF-660021" });

        var plan3 = new PaymentPlan
        {
            Patient = patients[0], Description = "Exámenes de laboratorio y consulta", TotalAmount = 2500m, DownPayment = 500m,
            NumberOfInstallments = 4, Frequency = PaymentFrequency.Weekly, StartDate = today.AddDays(-35),
            Status = PaymentPlanStatus.Completed,
        };
        for (var k = 0; k < 4; k++)
            plan3.Payments.Add(new Payment { Amount = 500m, PaidAt = today.AddDays(-35 + 7 * k), Method = PaymentMethod.Card, Reference = $"POS-77{k}210" });

        db.PaymentPlans.AddRange(plan1, plan2, plan3);
        await db.SaveChangesAsync(ct);
    }

    private static Patient P(string first, string last, string doc, DateOnly dob, Gender gender, string blood, string? allergies) => new()
    {
        FirstName = first, LastName = last, DocumentId = doc, DateOfBirth = dob, Gender = gender, BloodType = blood, Allergies = allergies,
        Phone = $"+504 9{doc[^4..]}-{doc[5..9]}", Email = $"{RemoveDiacritics(first.Split(' ')[0]).ToLowerInvariant()}.{RemoveDiacritics(last.Split(' ')[0]).ToLowerInvariant()}@correo.hn",
        Address = "Tegucigalpa, Francisco Morazán",
    };

    private static Medication M(string name, string dose, string company, string presentation, int stock, int min) => new()
    {
        CompoundName = name, Dose = dose, PharmaceuticalCompany = company, Presentation = presentation, Stock = stock, MinimumStock = min,
    };

    private static Diagnosis Dx(Patient p, string code, string description, DateOnly at, DiagnosisStatus status, Doctor doctor) => new()
    {
        Patient = p, Code = code, Description = description, DiagnosedAt = at, Status = status, Doctor = doctor,
    };

    private static MedicalRecord Rec(Patient p, DateTime visit, Doctor doctor, string reason, string? notes) => new()
    {
        Patient = p, VisitDate = visit, Doctor = doctor, Reason = reason, Notes = notes,
    };

    private static VitalSign V(Patient p, DateTime at, int sys, int dia, decimal glucose, int o2, int hr, decimal temp) => new()
    {
        Patient = p, RecordedAt = at, Systolic = sys, Diastolic = dia, Glucose = glucose, OxygenSaturation = o2, HeartRate = hr, Temperature = temp,
        CreatedBy = "Enf. Marta Herrera",
    };

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark);
        return new string(chars.ToArray()).Normalize(System.Text.NormalizationForm.FormC);
    }
}
