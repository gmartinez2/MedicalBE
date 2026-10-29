# MedicalBE — Hospital & Clinic Management API

ASP.NET Core (.NET 10) Web API backing the Medical System frontend.
The HTTP contract is defined in [`docs/API-CONTRACT.md`](docs/API-CONTRACT.md) and is the source of truth for both backend and frontend.

## Solution layout

| Project | Responsibility |
|---|---|
| `src/Medical.Domain` | Entities, enums, vital-sign clinical thresholds. No dependencies. |
| `src/Medical.Application` | DTOs, FluentValidation validators, service interfaces and implementations (EF Core projections via `IAppDbContext`). |
| `src/Medical.Infrastructure` | `ApplicationDbContext` (Identity + domain), EF configurations, migrations, local file storage, JWT issuing, seeding. |
| `src/Medical.Api` | Controllers, `Program.cs`, middleware (exceptions → ProblemDetails, security headers), auth policies, Swagger. |

Dependencies point inward: `Api → Infrastructure → Application → Domain`.

## Prerequisites

- .NET SDK 10.0.107+ (pinned in `global.json`, roll-forward on feature band)
- SQL Server Express reachable as `localhost\SQLEXPRESS` with Windows authentication
- Optional: `sqlcmd` for inspecting the database

## First-time setup

```bash
# 1. Restore the local EF Core tool (dotnet-ef 10, from .config/dotnet-tools.json)
dotnet tool restore

# 2. Configure the JWT signing key (never commit it). Generates a random 64-byte key:
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 64 | tr -d '\n')" --project src/Medical.Api
# PowerShell 7 alternative:
# dotnet user-secrets set "Jwt:Key" ([Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(64))) --project src/Medical.Api
```

If `Jwt:Key` is missing in **Development**, the API starts with an ephemeral random key and logs a warning
(tokens stop working after a restart). In any other environment the API refuses to start.

## Run

```bash
dotnet run --project src/Medical.Api
```

- API: `http://localhost:5080/api`
- Swagger UI: `http://localhost:5080/swagger` (Development only). Log in via `POST /api/auth/login`, then use **Authorize** with the token.
- CORS allows `http://localhost:4200` (configurable in `Cors:AllowedOrigins`).

In Development the API applies pending migrations and seeds data automatically at startup.

## Seeded credentials

| Email | Password | Role | Environment |
|---|---|---|---|
| `admin@hospital.local` | `Admin123!` | Admin | all |
| `doctor@hospital.local` | `Doctor123!` | Doctor | Development |
| `nurse@hospital.local` | `Nurse123!` | Nurse | Development |
| `billing@hospital.local` | `Billing123!` | Billing | Development |

Development also seeds 5 doctors, 10 patients, clinical history, vital signs (some abnormal),
shifts for the current week, 10 medications (2 low-stock) and 3 payment plans with payments.
Seeding is idempotent (skipped when doctors already exist).

## Database

- Connection string: `ConnectionStrings:Default` in `appsettings.json` (Windows auth, no secret).
- Migrations live in `src/Medical.Infrastructure/Persistence/Migrations`. A design-time factory means
  `dotnet ef` never boots the web host; override its connection with `MEDICAL_DB_CONNECTION`.

```bash
# Add a migration
dotnet ef migrations add <Name> --project src/Medical.Infrastructure --startup-project src/Medical.Infrastructure --output-dir Persistence/Migrations
# Apply
dotnet ef database update --project src/Medical.Infrastructure --startup-project src/Medical.Infrastructure
# Regenerate the idempotent schema script
dotnet ef migrations script --idempotent --project src/Medical.Infrastructure --startup-project src/Medical.Infrastructure --output database/schema.sql
```

`database/schema.sql` is the full idempotent schema (for DBAs / non-.NET deployment).

Key schema decisions:

- Money is `decimal(18,2)`; glucose and temperature are `decimal(5,1)`. Enums are stored as strings.
- All `DateTime` values are UTC (a value converter restores `DateTimeKind.Utc` on read).
- Patients are soft-deleted (`IsDeleted` + global query filter); their records, vitals, images and plans are hidden with them.
  `DocumentId` is unique among non-deleted patients (filtered unique index).
- Deleting a doctor cascades their shifts and nulls their references in clinical history.
- `PaymentPlans.RowVersion` guards concurrent payments against the same balance (loser gets 409).

## Behavior notes

- **Errors** are RFC 7807 ProblemDetails with user-facing messages in Spanish (the UI shows them verbatim).
  Validation errors are 400 with `errors: { camelCaseField: [messages] }`.
- **Payment plans**: installments are computed, never stored. Regular installment =
  `(total − downPayment) / n` truncated to cents; the last installment absorbs the remainder.
  Installment *k* is due at `startDate + (k − 1)` periods (weekly = 7 days, biweekly = 14 days, monthly = calendar month).
  `paidAmount = downPayment + Σ payments`, `balance = total − paidAmount`. A payment larger than the balance is 400.
  Plans become `Completed` when the balance reaches 0 and revert to `Active` if a payment is deleted; `Cancelled` is sticky.
- **Vital sign flags**: HighBloodPressure (sys ≥ 140 or dia ≥ 90), LowBloodPressure (sys < 90 or dia < 60),
  HighGlucose (> 180), LowGlucose (< 70), LowOxygen (< 92), Tachycardia (HR > 100), Fever (temp ≥ 38).
- **Images**: jpg/jpeg/png/webp/pdf up to 10 MB. Extension, declared content type and file signature must agree.
  Files are stored under `Storage:ImagesPath` (default `uploads/` in the API content root, git-ignored) as `{guid}{ext}`.
- **Dashboard** "today" / "on shift now" use UTC.
- Login is rate-limited to 10 attempts per minute per IP; accounts lock for 5 minutes after 5 failed passwords.
  Deactivated or deleted users are rejected even with an unexpired token.
