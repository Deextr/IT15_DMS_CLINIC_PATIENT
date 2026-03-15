using DMS_CPMS.Data;
using DMS_CPMS.Data.Models;
using DMS_CPMS.Data.Seeders;
using DMS_CPMS.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Enforce a 10 MB max request body size for file uploads
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10L * 1024 * 1024; // 10 MB
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 10L * 1024 * 1024; // 10 MB
});

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;

        // Password policy
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;

        // Lockout policy — brute-force protection
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddRoles<ApplicationRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddAuthorization(options =>
{
    // Require authenticated users by default
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Login/Login";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";

    // Validate security stamp every 1 minute so deactivated users are signed out quickly
    options.Events.OnValidatePrincipal = async context =>
    {
        // Run the default security stamp validation first
        await SecurityStampValidator.ValidatePrincipalAsync(context);

        if (context.Principal?.Identity?.IsAuthenticated == true)
        {
            var userManager = context.HttpContext.RequestServices
                .GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.GetUserAsync(context.Principal);
            if (user == null || !user.IsActive)
            {
                context.RejectPrincipal();
                await context.HttpContext.RequestServices
                    .GetRequiredService<SignInManager<ApplicationUser>>()
                    .SignOutAsync();
            }
        }
    };
});
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(1);
});
builder.Services.AddControllersWithViews();

// Register HttpContextAccessor for audit logging
builder.Services.AddHttpContextAccessor();

// Register Audit Log service
builder.Services.AddScoped<IAuditLogService, AuditLogService>();

// Register Google Drive service
builder.Services.AddSingleton<GoogleDriveService>();

// Register document conversion service (docx → PDF preview)
builder.Services.AddSingleton<DocumentConversionService>();

// Register Admin Dashboard service
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();

// Register Report services
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IReportExportService, ReportExportService>();

// Register AWS S3 export storage service
builder.Services.AddSingleton<IS3ExportStorageService, S3ExportStorageService>();

// QuestPDF Community License
QuestPDF.Settings.License = LicenseType.Community;

var app = builder.Build();

// Seed initial users
await DatabaseSeeder.SeedAsync(app.Services);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Security headers
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://apis.google.com https://accounts.google.com; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data: https://*.google.com https://*.googleapis.com https://*.gstatic.com; " +
        "frame-src 'self' https://docs.google.com https://drive.google.com https://accounts.google.com https://content.googleapis.com; " +
        "connect-src 'self' https://www.googleapis.com https://accounts.google.com;";
    await next();
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Login}/{action=Login}/{id?}");
app.MapRazorPages();

app.Run();
