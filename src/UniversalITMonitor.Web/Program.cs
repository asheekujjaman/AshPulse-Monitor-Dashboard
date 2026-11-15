using System.Threading.RateLimiting;
using System.Text.Json.Serialization;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using UniversalITMonitor.Application.Accounts;
using UniversalITMonitor.Application.Backups;
using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Application.Inventory;
using UniversalITMonitor.Application.Ipam;
using UniversalITMonitor.Application.NetworkMaps;
using UniversalITMonitor.Application.Notifications;
using UniversalITMonitor.Application.Monitoring;
using UniversalITMonitor.Application.Presentation;
using UniversalITMonitor.Application.Reports;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Monitoring;
using UniversalITMonitor.Core.Persistence;
using UniversalITMonitor.Monitoring;
using UniversalITMonitor.Storage.Json;
using UniversalITMonitor.Storage.Setup;
using UniversalITMonitor.Web.Infrastructure;
using UniversalITMonitor.Web.Logging;

var builder = WebApplication.CreateBuilder(args);
var configuredDataDirectory = builder.Configuration["Storage:DataDirectory"];
var webRoot = Path.GetFullPath(builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"));
var contentRoot = Path.GetFullPath(builder.Environment.ContentRootPath);
var dataDirectory = StoragePathResolver.ResolveDataDirectory(
    contentRoot,
    webRoot,
    configuredDataDirectory);
Directory.CreateDirectory(dataDirectory);

builder.Logging.ClearProviders();
builder.Logging.AddProvider(new RollingRedactedFileLoggerProvider(
    Path.Combine(dataDirectory, "logs"),
    builder.Configuration.GetValue("Logging:RollingFile:MaximumFileBytes", 10 * 1024 * 1024),
    builder.Configuration.GetValue("Logging:RollingFile:RetainedFiles", 30)));

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.Filters.Add(new ResponseCacheAttribute
    {
        Location = ResponseCacheLocation.None,
        NoStore = true
    });
}).AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var dataProtection = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")))
    .SetApplicationName("UniversalITMonitor");
var certificateThumbprint = builder.Configuration["DataProtection:CertificateThumbprint"];
X509Certificate2? dataProtectionCertificate = null;
if (!string.IsNullOrWhiteSpace(certificateThumbprint))
{
    using var certificateStore = new X509Store(StoreName.My, StoreLocation.LocalMachine);
    certificateStore.Open(OpenFlags.ReadOnly);
    dataProtectionCertificate = certificateStore.Certificates
        .Find(X509FindType.FindByThumbprint, certificateThumbprint, validOnly: false)
        .OfType<X509Certificate2>()
        .FirstOrDefault(certificate => certificate.HasPrivateKey)
        ?? throw new InvalidOperationException("The configured Data Protection certificate was not found with its private key in LocalMachine\\My.");
    dataProtection.ProtectKeysWithCertificate(dataProtectionCertificate);
}
var useWindowsDpapi = false;
if (dataProtectionCertificate is null && !builder.Environment.IsDevelopment() && OperatingSystem.IsWindows())
{
    useWindowsDpapi = true;
    dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: false);
}
var keyProtectionOptions = new BackupKeyProtectionOptions(
    dataProtectionCertificate is not null ? "Certificate" : useWindowsDpapi ? "WindowsDpapiCurrentUser" : "Default",
    dataProtectionCertificate?.Thumbprint,
    dataProtectionCertificate,
    useWindowsDpapi);

builder.Services.AddSingleton<IJsonDocumentStore>(_ => new FileJsonDocumentStore(dataDirectory));
builder.Services.AddSingleton<IApplicationMaintenanceLock, ApplicationMaintenanceLock>();
builder.Services.AddSingleton(keyProtectionOptions);
builder.Services.AddSingleton(sp => new BackupService(
    dataDirectory,
    webRoot,
    keyProtectionOptions,
    sp.GetRequiredService<IJsonDocumentStore>(),
    sp.GetRequiredService<IApplicationMaintenanceLock>(),
    sp.GetRequiredService<ILogger<BackupService>>(),
    contentRoot,
    builder.Environment.IsProduction()));
builder.Services.AddSingleton<IBackupService>(sp => sp.GetRequiredService<BackupService>());
builder.Services.AddSingleton<IUserRepository, JsonUserRepository>();
builder.Services.AddSingleton<IRoleRepository, JsonRoleRepository>();
builder.Services.AddSingleton<IDeviceRepository, JsonDeviceRepository>();
builder.Services.AddSingleton<IInventoryStructureRepository, JsonInventoryStructureRepository>();
builder.Services.AddSingleton<INetworkMapRepository, JsonNetworkMapRepository>();
builder.Services.AddSingleton<IIpamRepository, JsonIpamRepository>();
builder.Services.AddSingleton<INotificationRepository, JsonNotificationRepository>();
builder.Services.AddSingleton<IMonitoringHistoryStore>(_ => new JsonMonitoringHistoryStore(dataDirectory));
builder.Services.AddSingleton<IMonitoringSampleStore>(_ => new JsonMonitoringSampleStore(dataDirectory));
builder.Services.AddSingleton<ISnmpMetricsStore>(_ => new JsonSnmpMetricsStore(dataDirectory));
builder.Services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
builder.Services.AddSingleton<INotificationSecretProtector, NotificationSecretProtector>();
builder.Services.AddSingleton<ITelegramNotificationSender, TelegramNotificationSender>();
builder.Services.AddSingleton<IEmailNotificationSender, EmailNotificationSender>();
builder.Services.AddSingleton<IDeviceService, DeviceService>();
builder.Services.AddSingleton<IInventoryStructureService, InventoryStructureService>();
builder.Services.AddSingleton<INetworkMapService, NetworkMapService>();
builder.Services.AddSingleton<IIpamService, IpamService>();
builder.Services.AddSingleton<INotificationService, NotificationService>();
builder.Services.AddSingleton<IReportService, ReportService>();
builder.Services.AddSingleton<IMonitoringCycleTrigger, MonitoringCycleTrigger>();
builder.Services.AddSingleton<IIcmpProbe, IcmpProbe>();
builder.Services.AddSingleton<ISnmpMetricsCollector, SnmpMetricsCollector>();
builder.Services.AddSingleton<IPasswordHasher<LocalUser>, PasswordHasher<LocalUser>>();
builder.Services.AddSingleton<InitialDataSeeder>();
builder.Services.AddSingleton<LegacyHierarchyMigrator>();
builder.Services.AddSingleton<InventoryReferenceReconciler>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<IUserAdministrationService, UserAdministrationService>();
builder.Services.AddScoped<IAppPresentationService, AppPresentationService>();
builder.Services.AddHostedService<MonitoringWorker>();
builder.Services.AddHostedService<NotificationDeliveryWorker>();
builder.Services.AddHostedService<AutomaticBackupWorker>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "UniversalITMonitor.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = async context =>
        {
            var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var users = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
            LocalUser? current;
            var isBackupMutation = HttpMethods.IsPost(context.Request.Method) &&
                context.Request.Path.StartsWithSegments("/Backups") &&
                (context.Request.Path.Value?.EndsWith("/SaveSettings", StringComparison.OrdinalIgnoreCase) == true ||
                 context.Request.Path.Value?.EndsWith("/Create", StringComparison.OrdinalIgnoreCase) == true ||
                 context.Request.Path.Value?.EndsWith("/Restore", StringComparison.OrdinalIgnoreCase) == true);
            if (isBackupMutation)
            {
                await using var maintenance = await context.HttpContext.RequestServices
                    .GetRequiredService<IApplicationMaintenanceLock>()
                    .EnterReadAsync(context.HttpContext.RequestAborted);
                current = string.IsNullOrWhiteSpace(userId)
                    ? null
                    : await users.FindByIdAsync(userId, context.HttpContext.RequestAborted);
            }
            else
            {
                current = string.IsNullOrWhiteSpace(userId)
                    ? null
                    : await users.FindByIdAsync(userId, context.HttpContext.RequestAborted);
            }
            if (current is null || !current.IsEnabled)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            var roles = current.Roles
                .Where(role => role is SystemRoles.Administrator or SystemRoles.Operator or SystemRoles.ReadOnly)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (roles.Length == 0)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, current.Id));
            identity.AddClaim(new Claim(ClaimTypes.Name, current.UserName));
            identity.AddClaim(new Claim(ClaimTypes.GivenName, current.DisplayName));
            foreach (var role in roles) identity.AddClaim(new Claim(ClaimTypes.Role, role));
            context.ReplacePrincipal(new ClaimsPrincipal(identity));
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.AddPolicy("CanManageSettings", policy => policy.RequireRole(SystemRoles.Administrator));
    options.AddPolicy("CanManageDevices", policy => policy.RequireRole(SystemRoles.Administrator, SystemRoles.Operator));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

var app = builder.Build();

await using var instanceLease = await ApplicationInstanceLease.AcquireAsync(dataDirectory);

await using (var initializationScope = app.Services.CreateAsyncScope())
{
    var seeder = initializationScope.ServiceProvider.GetRequiredService<InitialDataSeeder>();
    await seeder.InitializeAsync();
    var migrator = initializationScope.ServiceProvider.GetRequiredService<LegacyHierarchyMigrator>();
    await migrator.MigrateAsync();
    var reconciler = initializationScope.ServiceProvider.GetRequiredService<InventoryReferenceReconciler>();
    await reconciler.ReconcileAsync();
}

await app.Services.GetRequiredService<IBackupService>().GetSettingsAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    var isBackupMutation = HttpMethods.IsPost(context.Request.Method) &&
        context.Request.Path.StartsWithSegments("/Backups") &&
        (context.Request.Path.Value?.EndsWith("/SaveSettings", StringComparison.OrdinalIgnoreCase) == true ||
         context.Request.Path.Value?.EndsWith("/Create", StringComparison.OrdinalIgnoreCase) == true ||
         context.Request.Path.Value?.EndsWith("/Restore", StringComparison.OrdinalIgnoreCase) == true);
    if (isBackupMutation)
    {
        await next();
        return;
    }

    var maintenanceLock = context.RequestServices.GetRequiredService<IApplicationMaintenanceLock>();
    await using var maintenance = await maintenanceLock.EnterReadAsync(context.RequestAborted);
    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.MapStaticAssets().AllowAnonymous();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

public partial class Program { }
