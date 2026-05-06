# Project Security Documentation Handbook — DMS_CPMS

**Course:** IT16/L — Information Assurance and Security 1  

This document answers sections **1–12** of the Technical Writing rubric for **DMS_CPMS**. Add screenshots and scan outputs where the rubric requires them.

---

## 1. Project Overview

**Purpose.** DMS_CPMS is a **clinic-oriented document and patient management system**. It supports patient records, document uploads and versioning, archiving and retention, reporting, exports (including optional **AWS S3** upload and **Google Drive** integration), and **audit logging** of important actions.

**Intended users.** The application uses **role-based staff accounts**: **SuperAdmin**, **Admin**, and **Staff**, each with different dashboards and controller access.

**Platform and language.** The solution targets **.NET 8** as an **ASP.NET Core MVC** web app with **Razor views**, **C#**, **Entity Framework Core**, and **SQL Server**. Supporting libraries include AWSSDK.S3, Google Drive APIs, QuestPDF, ClosedXML, and others (see `DMS_CPMS.csproj`).

---

## 2. Secure Coding Practices

**Avoiding hardcoded credentials.** Sensitive settings are not committed as real secrets in the shared `appsettings.json` (placeholders for connection string, AWS, and Google settings). The project includes a **UserSecretsId** for local development. Services such as `S3ExportStorageService` read configuration at runtime and fail clearly if required keys are missing.

**Sample secure patterns**

- Anti-forgery on state-changing requests (example: login POST in `LoginController`):

```csharp
[HttpPost]
[AllowAnonymous]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
```

- Security headers and Content-Security-Policy in `Program.cs` (`X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, CSP).

**Screenshots (for submission).** User Secrets / environment configuration (redacted), and a controller action showing `[ValidateAntiForgeryToken]`.

---

## 3. Authentication and Authorization

**Login and registration.** Primary login is **`/Login/Login`** via `LoginController`, using `SignInManager.PasswordSignInAsync` with **`lockoutOnFailure: true`**. Deactivated users are blocked and audit-logged. Successful login, failed login, lockout, and logout are logged. Staff accounts are created through **Admin** and **SuperAdmin** account management (`UserManager.CreateAsync`). The **Identity** area includes Register / Forgot password / Reset password pages if enabled in deployment.

**Password hashing.** ASP.NET Core Identity stores **PBKDF2-based password hashes** in **`PasswordHash`** — not plaintext.

**Roles and access.** A **fallback authorization policy** requires an authenticated user by default. Controllers use **`[Authorize(Roles = "...")]`** to limit modules (e.g. patient and document features for SuperAdmin, Admin, Staff; tighter rules for Google Drive, archive/retention, and reports).

**Screenshots.** Login page; access denied or restricted URL as a lower-privilege user; Identity / role data as appropriate.

---

## 4. Data Encryption

**What is protected**

- **Passwords:** Identity **hashes** in the database.
- **Data in transit:** **`UseHttpsRedirection`**; in non-development, **`UseHsts`**.
- **Exports on S3:** **Server-side encryption (SSE-S3 / AES256)** on `PutObject` in `S3ExportStorageService`.

**Note for documentation.** Patient demographic fields in SQL are **normal columns**, not application-level encrypted fields. For “encrypted data in the database” screenshots, use **`PasswordHash`** (and optionally describe **SQL Server TDE** or hosting encryption if applicable).

---

## 5. Input Validation and Sanitization

**Validated inputs**

- View models: **`[Required]`**, **`[StringLength]`**, **`[RegularExpression]`** (e.g. gender), **`[DataType]`**, **`[Compare]`** for passwords.
- Custom validation: **`ValidBirthDate`**, **`MaxFileSize`** (10 MB uploads, consistent with Kestrel/form limits in `Program.cs`).
- Controllers check **`ModelState`** before processing.

**Tools.** DataAnnotations, custom attributes, EF Core model constraints, Razor output encoding.

**Screenshots.** Failed validation (invalid gender, oversized file, weak password on account creation, etc.).

---

## 6. Error Handling and Logging

**Errors**

- **Production:** `UseExceptionHandler("/Home/Error")` and **HSTS** in `Program.cs`. Shared view: `Views/Shared/Error.cshtml`. Ensure a matching **`HomeController.Error`** action exists (or update the exception handler path) so production errors resolve correctly.
- **Development:** `UseMigrationsEndPoint` and database developer page exception filter.

**Logging**

- **`ILogger`** in services and controllers (e.g. S3, Google Drive, reports, audit failures).
- **Audit trail:** `AuditLog` via **`IAuditLogService`** (login, failed login, lockout, logout, and other actions).

**Screenshots.** Log output with an error entry; audit log UI or database rows for failed login / login.

---

## 7. Access Control

**Protected pages.** Default policy requires authentication; anonymous access only where **`[AllowAnonymous]`** is applied (e.g. login). Feature areas are restricted by **role** (SuperAdmin, Admin, Staff).

**Preventing unauthorized access.** Cookie authentication, authorization attributes, login path **`/Login/Login`**, access denied path configured on the application cookie. **Inactive** users are rejected and can be signed out via **security stamp** validation (configured in `Program.cs`).

**Proof.** Two test accounts: attempt to open a SuperAdmin-only URL as Staff; expect access denied or redirect.

---

## 8. Code Auditing Tools

The repo does not pin a single commercial scanner. Typical choices for this stack:

- Visual Studio / Rider built-in analyzers; **nullable reference types** enabled.
- **SonarLint** or NuGet security analyzers.
- **`dotnet build`** and CI warning gates.

**Screenshots.** Analyzer or SonarLint results. **Vulnerabilities:** summarize findings (e.g. misconfiguration, dependency updates, authorization gaps) and fixes applied.

---

## 9. Testing

**Automated unit tests (xUnit).** The solution includes **`DMS_CPMS.Tests`**, a **.NET 8** test project referencing the web app. Packages: **xUnit**, **xunit.runner.visualstudio**, **Microsoft.NET.Test.Sdk**, **coverlet.collector** (from the standard template).

Run all tests from the solution folder:

```text
dotnet test DMS_CPMS.Tests\DMS_CPMS.Tests.csproj
```

**What the unit tests cover (examples tied to security-relevant validation):**

| Test class | Focus |
|------------|--------|
| `ValidBirthDateAttributeTests` | Birth date not in the future, not before 1900 |
| `MaxFileSizeAttributeTests` | Upload size limit enforcement (aligned with the 10 MB app limit) |
| `CreateAccountViewModelValidationTests` | Password complexity, confirm password, role, username rules |
| `CreatePatientViewModelValidationTests` | Allowed gender values |

*Note:* The web project **`DMS_CPMS.csproj`** excludes the `DMS_CPMS.Tests\**` folder from its compile glob so test sources are not built into the production app.

**Manual / integration testing (recommended in addition):**

- Login, lockout, deactivated user.
- Patient/document flows per role.
- Upload limits and invalid inputs in the browser.
- Report export and S3 (if configured).

**Tools.** **xUnit** + `dotnet test`; browser for end-to-end UI; **Postman** only if you test specific HTTP endpoints.

**Screenshots.** Terminal or Test Explorer showing **passed** xUnit tests; optional manual test checklist; Postman only if used.

---

## 10. Security Policies

**Password policy (as configured in code).** Minimum **8** characters; requires uppercase, lowercase, digit, and non-alphanumeric character (`IdentityOptions` in `Program.cs`). Account view models align complexity rules.  
*If the rubric requires **10** characters, increase `RequiredLength` and matching `MinimumLength` in view models.*

**Login attempt policy.** **5** failed attempts → **15-minute** lockout; failures and lockouts logged in the audit log.

**Data handling policy.** HTTPS; role-based access to patient/document data; audit logging; S3 SSE for stored exports when used; no anonymous public exposure of patient data.

---

## 11. Incident Response Plan

- **Detection.** Application and hosting logs; audit log patterns (failed logins, lockouts); alerts from the hosting provider.
- **Reporting.** Users report to clinic IT / SuperAdmin with time, account, and symptoms.
- **Containment.** Disable accounts (`IsActive` / lockout); rotate API keys if cloud credentials are suspected; use firewall/hosting blocks if available.
- **Recovery.** Database restore from backup; redeploy known-good build; password resets for affected users; audit log review for scope.

---

## 12. Security Compliance Handbook (System Rules and Standards)

**PASSWORD POLICY**

- Passwords must be at least **8** characters (or **10** if you align code and policy with the rubric example).
- Passwords must include at least one **uppercase** letter, one **lowercase** letter, one **number**, and one **special** character.
- Passwords must not be stored or logged in plaintext; only Identity **hashes** are stored.
- *(Optional organizational rule:)* Passwords should not contain the user’s **full name** or **username**; **90-day rotation** may be required by clinic policy (not auto-enforced in the current codebase unless you add it).

**LOGIN ATTEMPT POLICY**

- **Five** failed login attempts lock the account for **15 minutes**.
- Failed attempts and lockouts must be **logged** in the system audit log where the user is known.

**DATA HANDLING POLICY**

- **HTTPS** is required in production.
- **Personal and patient data** is available only to **authenticated** users with appropriate **roles**.
- **S3** report objects use **server-side encryption** when that feature is used.
- Personal information must not be displayed on **public** pages.

**ACCESS CONTROL POLICY**

- **SuperAdmin**, **Admin**, and **Staff** capabilities are separated by **authorization** on controllers and areas.
- Only authorized roles may access **configuration**, **reports**, **Google Drive**, and **archive/retention** features as implemented.
- Unauthorized access attempts should result in **access denied** or redirect to **login**.

**LOGGING AND MONITORING POLICY**

- Security-relevant events (login, logout, failed login, lockout, and actions wired to `IAuditLogService`) are recorded with **timestamp** and **role** where applicable.
- Administrators should **review** audit logs regularly.

**BACKUP AND RECOVERY POLICY**

- **SQL Server** (and hosting) backups follow clinic or provider schedule (e.g. at least **weekly** minimum per organizational standard).

**COMPLIANCE DECLARATION**

By submitting this project, the student confirms that the security policies above accurately describe the **implemented** controls in DMS_CPMS, and that any **planned** items not yet in code (e.g. mandatory 90-day password expiry) are documented separately if required by the instructor.

---

*Generated for DMS_CPMS — align section 12 and password length with your instructor’s rubric if they require exact wording (e.g. 10 characters, 90-day rotation).*
