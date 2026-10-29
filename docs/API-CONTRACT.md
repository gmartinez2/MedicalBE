# Medical System — API Contract (source of truth for BE and FE)

Base URL (local): `http://localhost:5080/api`  ·  Frontend dev origin: `http://localhost:4200`
JSON: camelCase. Enums serialized as strings. Dates: ISO 8601 (`DateOnly` → `"2026-10-29"`, `DateTime` → UTC `"2026-10-29T14:00:00Z"`).
Errors: RFC 7807 ProblemDetails (`{ type, title, status, detail, errors? }`); validation → 400 with `errors: { field: [msg] }`.
Auth: `Authorization: Bearer <jwt>` on every endpoint except `POST /auth/login`.

## Paging

List endpoints accept `?search=&page=1&pageSize=20` and return:
```json
{ "items": [], "totalCount": 0, "page": 1, "pageSize": 20 }
```

## Roles

`Admin`, `Doctor`, `Nurse`, `Billing`. Seeded user: `admin@hospital.local` / `Admin123!` (role Admin).

| Area | Read | Write |
|---|---|---|
| Patients | all roles | Admin, Doctor, Nurse |
| Records / Diagnoses / Treatments | Admin, Doctor, Nurse | Admin, Doctor |
| Images | Admin, Doctor, Nurse | Admin, Doctor, Nurse |
| Vital signs | Admin, Doctor, Nurse | Admin, Doctor, Nurse |
| Doctors / Shifts | all roles | Admin |
| Payment plans / Payments | Admin, Billing | Admin, Billing |
| Medications | all roles | Admin, Doctor |
| Users | Admin | Admin |
| Reports: medical history | Admin, Doctor, Nurse | — |
| Reports: balances | Admin, Billing | — |

## Auth & Users (ASP.NET Core Identity + JWT)

- `POST /auth/login` `{ email, password }` → `{ token, expiresAt, user: UserDto }`
- `GET /auth/me` → `UserDto`
- `GET /users?search=&page=&pageSize=` → `Paged<UserDto>`
- `POST /users` `{ email, fullName, password, roles: string[] }` → `UserDto`
- `PUT /users/{id}` `{ fullName, roles: string[], isActive }` → `UserDto`
- `DELETE /users/{id}` → 204

`UserDto = { id: string, email, fullName, roles: string[], isActive: boolean }`

## Patients

- `GET /patients?search=` (search matches first/last name, documentId) → `Paged<PatientDto>`
- `GET /patients/{id}` → `PatientDto`
- `POST /patients` / `PUT /patients/{id}` body `PatientInput` → `PatientDto`
- `DELETE /patients/{id}` → 204 (soft delete)

```
PatientInput = { firstName, lastName, documentId, dateOfBirth: DateOnly, gender: "Male"|"Female"|"Other",
                 phone?, email?, address?, bloodType?, allergies? }
PatientDto   = PatientInput + { id: number, fullName, age: number, createdAt }
```

## Medical history (per patient)

- `GET /patients/{id}/records` → `MedicalRecordDto[]` · `POST /patients/{id}/records` · `PUT /records/{id}` · `DELETE /records/{id}`
  `MedicalRecordInput = { visitDate: DateTime, doctorId?: number, reason, notes? }`
  `MedicalRecordDto = input + { id, patientId, doctorName? }`
- `GET /patients/{id}/diagnoses` → `DiagnosisDto[]` · `POST /patients/{id}/diagnoses` · `PUT /diagnoses/{id}` · `DELETE /diagnoses/{id}`
  `DiagnosisInput = { code?, description, diagnosedAt: DateOnly, status: "Active"|"Resolved"|"Chronic", doctorId?: number, notes? }`
  `DiagnosisDto = input + { id, patientId, doctorName? }`
- `GET /patients/{id}/treatments` → `TreatmentDto[]` · `POST /patients/{id}/treatments` · `PUT /treatments/{id}` · `DELETE /treatments/{id}`
  `TreatmentInput = { description, diagnosisId?: number, medicationId?: number, doctorId?: number, startDate: DateOnly, endDate?: DateOnly, instructions?, status: "Planned"|"InProgress"|"Completed"|"Suspended" }`
  `TreatmentDto = input + { id, patientId, diagnosisDescription?, medicationName?, doctorName? }`

## Medical images

- `GET /patients/{id}/images` → `MedicalImageDto[]`
- `POST /patients/{id}/images` multipart/form-data: `file` (jpg/jpeg/png/webp/pdf, max 10 MB), `title`, `studyType`, `studyDate` (DateOnly), `description?` → `MedicalImageDto`
- `GET /images/{id}/file` → binary stream with correct Content-Type (requires Bearer; FE fetches as blob)
- `DELETE /images/{id}` → 204

`MedicalImageDto = { id, patientId, title, studyType, studyDate, description?, fileName, contentType, sizeBytes, uploadedAt, uploadedBy }`
Files stored on disk under `Storage:ImagesPath` (default `./uploads`), never in git. DB stores metadata + relative path.

## Vital signs

- `GET /patients/{id}/vitals?from=&to=` → `VitalSignDto[]` ordered by recordedAt desc
- `POST /patients/{id}/vitals` · `DELETE /vitals/{id}`
  `VitalSignInput = { recordedAt: DateTime, systolic: int(50-260), diastolic: int(30-160), glucose: decimal mg/dL (20-600), oxygenSaturation: int % (50-100), heartRate?: int (20-250), temperature?: decimal °C (30-45), notes? }`
  `VitalSignDto = input + { id, patientId, recordedBy, flags: string[] }` — flags computed server-side, e.g. `"HighBloodPressure"`, `"LowOxygen"`, `"HighGlucose"`, `"LowGlucose"`.

## Doctors & shifts

- `GET /doctors?search=` → `Paged<DoctorDto>` · `GET /doctors/{id}` · `POST /doctors` · `PUT /doctors/{id}` · `DELETE /doctors/{id}`
  `DoctorInput = { firstName, lastName, specialty, licenseNumber, phone?, email? }` · `DoctorDto = input + { id, fullName }`
- `GET /shifts?from=&to=&doctorId=` → `ShiftDto[]` · `POST /shifts` · `PUT /shifts/{id}` · `DELETE /shifts/{id}`
  `ShiftInput = { doctorId, startTime: DateTime, endTime: DateTime, location, notes? }` (end > start; no overlap per doctor → 409)
  `ShiftDto = input + { id, doctorName, specialty }`

## Billing (payment plans)

- `GET /payment-plans?search=&status=` → `Paged<PaymentPlanDto>` · `GET /payment-plans/{id}` → `PaymentPlanDetailDto`
- `GET /patients/{id}/payment-plans` → `PaymentPlanDto[]`
- `POST /payment-plans` · `PUT /payment-plans/{id}` · `DELETE /payment-plans/{id}`
  `PaymentPlanInput = { patientId, description, totalAmount: decimal, downPayment: decimal, numberOfInstallments: int (1-120), frequency: "Weekly"|"Biweekly"|"Monthly", startDate: DateOnly, terms?: string (free text, up to 4000 chars), status: "Active"|"Completed"|"Cancelled" }`
  `PaymentPlanDto = input + { id, patientName, paidAmount, balance, installmentAmount, createdAt }`
  `PaymentPlanDetailDto = PaymentPlanDto + { installments: { number, dueDate, amount }[], payments: PaymentDto[] }` (installments computed from plan)
- `POST /payment-plans/{id}/payments` `{ amount, paidAt: DateOnly, method: "Cash"|"Card"|"Transfer"|"Other", reference? }` → `PaymentDto` (amount ≤ balance → else 400; plan auto-Completed when balance hits 0)
- `DELETE /payments/{id}` → 204
  `PaymentDto = { id, paymentPlanId, amount, paidAt, method, reference?, createdBy }`

## Medications (inventory)

- `GET /medications?search=` → `Paged<MedicationDto>` · `GET /medications/{id}` · `POST /medications` · `PUT /medications/{id}` · `DELETE /medications/{id}`
  `MedicationInput = { compoundName, dose, pharmaceuticalCompany, presentation?, stock: int ≥ 0, minimumStock: int ≥ 0 }`
  `MedicationDto = input + { id, isLowStock: boolean }`

## Reports

- `GET /reports/medical-history?patientName=` → `MedicalHistoryReportDto[]` (one per matching patient, name match is contains/case-insensitive on full name)
  `MedicalHistoryReportDto = { patient: PatientDto, records: MedicalRecordDto[], diagnoses: DiagnosisDto[], treatments: TreatmentDto[], vitals: VitalSignDto[], images: MedicalImageDto[] }`
- `GET /reports/balances?patientName=&onlyWithBalance=true` → `{ rows: BalanceRowDto[], totals: { totalAmount, paidAmount, balance } }`
  `BalanceRowDto = { patientId, patientName, documentId, activePlans, totalAmount, paidAmount, balance, lastPaymentAt? }`

## Dashboard

- `GET /dashboard/summary` → `{ patients, doctorsOnShiftNow, shiftsToday, activePaymentPlans, outstandingBalance, lowStockMedications, recentVitalAlerts: VitalSignDto[] (with patientName) }`
  (`recentVitalAlerts` items add `patientName`.)
