using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Tests;

[TestClass]
public sealed class Phase2AuthorizationAndCsvTests
{
    [TestMethod]
    public async Task AdminCanManageDevicesAndMonitoringSettings()
    {
        using var directory = new TemporaryTestDirectory();
        await using var factory = new TestWebApplicationFactory(directory.Path);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var create = Request(HttpMethod.Get, "/Devices/Create", "Administrator");
        using var settings = Request(HttpMethod.Get, "/MonitoringSettings", "Administrator");

        Assert.AreEqual(HttpStatusCode.OK, (await client.SendAsync(create)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.SendAsync(settings)).StatusCode);
    }

    [TestMethod]
    public async Task ReadOnlyCanViewInventoryButCannotManageDevicesOrSettings()
    {
        using var directory = new TemporaryTestDirectory();
        await using var factory = new TestWebApplicationFactory(directory.Path);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var inventory = Request(HttpMethod.Get, "/Devices", "ReadOnly");
        using var create = Request(HttpMethod.Get, "/Devices/Create", "ReadOnly");
        using var settings = Request(HttpMethod.Get, "/MonitoringSettings", "ReadOnly");
        using var anonymous = new HttpRequestMessage(HttpMethod.Get, "/Devices");

        Assert.AreEqual(HttpStatusCode.OK, (await client.SendAsync(inventory)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.SendAsync(create)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.SendAsync(settings)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.SendAsync(anonymous)).StatusCode);
    }

    [TestMethod]
    public async Task LegacyOceanBlueDefaultRendersAshPulsePaletteWithoutRewritingStoredTheme()
    {
        using var directory = new TemporaryTestDirectory();
        await using var factory = new TestWebApplicationFactory(directory.Path);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var documents = factory.Services.GetRequiredService<IJsonDocumentStore>();
        await documents.WriteAsync("themes.json", new ThemeSettings
        {
            Name = ThemePresetCatalog.OceanBlue,
            PrimaryColor = "#2563eb",
            SidebarColor = "#101b31",
            SurfaceColor = "#ffffff",
            PageColor = "#f4f7fb",
            HeaderColor = "#ffffff",
            BorderColor = "#e7ebf2",
            InkColor = "#18243a",
            MutedColor = "#77849a",
            SoftColor = "#edf3fc"
        });

        using var request = Request(HttpMethod.Get, "/Home", "Administrator");
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(html, "--brand-primary: #0078D4");
        StringAssert.Contains(html, "--brand-sidebar: #0B2D5B");
        StringAssert.Contains(html, "--brand-page: #F4F8FD");

        var storedTheme = await documents.ReadAsync("themes.json", static () => new ThemeSettings());
        Assert.AreEqual("#2563eb", storedTheme.PrimaryColor);
    }

    [TestMethod]
    public async Task IpamAndTopologyAuthorization_AllowsReadOnlyViewingAndAdministratorManagement()
    {
        using var directory = new TemporaryTestDirectory();
        await using var factory = new TestWebApplicationFactory(directory.Path);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var adminIpam = Request(HttpMethod.Get, "/Ipam/Create", "Administrator");
        using var adminMapEditor = Request(HttpMethod.Get, "/NetworkMaps/Editor", "Administrator");
        using var readOnlyIpam = Request(HttpMethod.Get, "/Ipam", "ReadOnly");
        using var readOnlySubnets = Request(HttpMethod.Get, "/Ipam/Subnets", "ReadOnly");
        using var readOnlyIpamCreate = Request(HttpMethod.Get, "/Ipam/Create", "ReadOnly");
        using var readOnlyIpamImport = Request(HttpMethod.Get, "/Ipam/Import", "ReadOnly");
        using var readOnlyMap = Request(HttpMethod.Get, "/NetworkMaps", "ReadOnly");
        using var dashboard = Request(HttpMethod.Get, "/Home", "ReadOnly");
        using var globalSearch = Request(HttpMethod.Get, "/Search?q=192.0.2.1", "ReadOnly");

        Assert.AreEqual(HttpStatusCode.OK, (await client.SendAsync(adminIpam)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.SendAsync(adminMapEditor)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.SendAsync(readOnlyIpam)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.SendAsync(readOnlySubnets)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.SendAsync(readOnlyIpamCreate)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.SendAsync(readOnlyIpamImport)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.SendAsync(readOnlyMap)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.SendAsync(dashboard)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.SendAsync(globalSearch)).StatusCode);
    }

    [TestMethod]
    public async Task Phase4PagesAndExports_RequireAdministratorForManagementAndAllowReadOnlyReports()
    {
        using var directory = new TemporaryTestDirectory();
        await using var factory = new TestWebApplicationFactory(directory.Path);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var adminUsers = Request(HttpMethod.Get, "/UserAdministration", "Administrator");
        using var readOnlyUsers = Request(HttpMethod.Get, "/UserAdministration", "ReadOnly");
        using var readOnlyUserPost = Request(HttpMethod.Post, "/UserAdministration/Create", "ReadOnly");
        using var adminNotifications = Request(HttpMethod.Get, "/Notifications", "Administrator");
        using var readOnlyNotifications = Request(HttpMethod.Get, "/Notifications", "ReadOnly");
        using var adminBackups = Request(HttpMethod.Get, "/Backups", "Administrator");
        using var adminBackupCreateWithoutToken = Request(HttpMethod.Post, "/Backups/Create", "Administrator");
        using var readOnlyBackups = Request(HttpMethod.Get, "/Backups", "ReadOnly");
        using var readOnlyBackupCreate = Request(HttpMethod.Post, "/Backups/Create", "ReadOnly");
        using var adminAppearance = Request(HttpMethod.Get, "/Appearance", "Administrator");
        using var readOnlyAppearance = Request(HttpMethod.Get, "/Appearance", "ReadOnly");
        using var adminIntegrity = Request(HttpMethod.Get, "/MonitoringSettings/Integrity", "Administrator");
        using var readOnlyIntegrity = Request(HttpMethod.Get, "/MonitoringSettings/Integrity", "ReadOnly");
        using var readOnlyReports = Request(HttpMethod.Get, "/Reports", "ReadOnly");
        using var readOnlyExport = Request(HttpMethod.Get, "/Reports/Export?kind=Availability&startDate=2026-01-01&endDate=2026-01-01", "ReadOnly");

        Assert.AreEqual(HttpStatusCode.OK, (await client.SendAsync(adminUsers)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.SendAsync(readOnlyUsers)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.SendAsync(readOnlyUserPost)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.SendAsync(adminNotifications)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.SendAsync(readOnlyNotifications)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.SendAsync(adminBackups)).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.SendAsync(adminBackupCreateWithoutToken)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.SendAsync(readOnlyBackups)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.SendAsync(readOnlyBackupCreate)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.SendAsync(adminAppearance)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.SendAsync(readOnlyAppearance)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.SendAsync(adminIntegrity)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.SendAsync(readOnlyIntegrity)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.SendAsync(readOnlyReports)).StatusCode);
        using var export = await client.SendAsync(readOnlyExport);
        Assert.AreEqual(HttpStatusCode.OK, export.StatusCode);
        Assert.AreEqual("text/csv", export.Content.Headers.ContentType?.MediaType);
    }

    [TestMethod]
    public async Task EveryThemeRendersAcrossApplicationPagesAndFaviconAssetsLoad()
    {
        using var directory = new TemporaryTestDirectory();
        await using var factory = new TestWebApplicationFactory(directory.Path);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var documents = factory.Services.GetRequiredService<UniversalITMonitor.Core.Persistence.IJsonDocumentStore>();
        await documents.WriteAsync("users.json", new UserDocument
        {
            Users = [new LocalUser { Id = "theme-login", UserName = "theme-login", DisplayName = "Theme Login" }]
        });
        var pagePaths = new[]
        {
            "/Home", "/Devices", "/Locations", "/Groups", "/NetworkMaps", "/NetworkMaps/Editor",
            "/Ipam", "/Reports", "/MonitoringSettings", "/Appearance", "/UserAdministration", "/Backups"
        };

        foreach (var theme in UniversalITMonitor.Core.Models.ThemePresetCatalog.Names)
        {
            var preset = UniversalITMonitor.Core.Models.ThemePresetCatalog.Create(theme);
            await documents.WriteAsync("themes.json", preset);
            foreach (var path in pagePaths)
            {
                using var request = Request(HttpMethod.Get, path, "Administrator");
                using var response = await client.SendAsync(request);
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"{theme} page failed: {path}");
                var html = await response.Content.ReadAsStringAsync();
                StringAssert.Contains(html, $"data-theme=\"{UniversalITMonitor.Core.Models.ThemePresetCatalog.Key(theme)}\"");
                StringAssert.Contains(html, $"--brand-primary: {preset.PrimaryColor}");
            }

            using var loginRequest = Request(HttpMethod.Get, "/Account/Login", role: null);
            using var loginResponse = await client.SendAsync(loginRequest);
            Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
            var loginHtml = await loginResponse.Content.ReadAsStringAsync();
            StringAssert.Contains(loginHtml, "class=\"auth-brand\"");
            StringAssert.Contains(loginHtml, "favicon.svg");
            StringAssert.Contains(loginHtml, $"data-theme=\"{UniversalITMonitor.Core.Models.ThemePresetCatalog.Key(theme)}\"");
        }

        foreach (var (path, contentType) in new[]
        {
            ("/favicon.svg", "image/svg+xml"), ("/favicon-32.png", "image/png"),
            ("/favicon-192.png", "image/png"), ("/favicon-512.png", "image/png"),
            ("/apple-touch-icon.png", "image/png"), ("/favicon.ico", "image/x-icon"),
            ("/site.webmanifest", "application/manifest+json"),
            ("/images/brand/logo-horizontal-light.svg", "image/svg+xml"),
            ("/images/brand/logo-horizontal-dark.svg", "image/svg+xml"),
            ("/images/brand/ashpulse-app-icon.svg", "image/svg+xml"),
            ("/images/brand/sidebar-icon.svg", "image/svg+xml")
        })
        {
            using var response = await client.GetAsync(path);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"Favicon asset failed: {path}");
            Assert.AreEqual(contentType, response.Content.Headers.ContentType?.MediaType, $"Favicon MIME type failed: {path}");
            Assert.IsGreaterThan(0, (await response.Content.ReadAsByteArrayAsync()).Length);
        }

        using var cssResponse = await client.GetAsync("/css/site.css");
        Assert.AreEqual(HttpStatusCode.OK, cssResponse.StatusCode);
        var css = await cssResponse.Content.ReadAsStringAsync();
        StringAssert.Contains(css, ".status-up .status-dot { background: #16A36A; }");
        StringAssert.Contains(css, ".status-down .status-dot { background: #DC3545; }");
        StringAssert.Contains(css, ".status-warning {");
        StringAssert.Contains(css, ".status-unknown .status-dot { background: #94A3B8; }");
        StringAssert.Contains(css, "@media (max-width: 420px)");
        StringAssert.Contains(css, "@media (max-width: 760px)");
    }

    [TestMethod]
    public async Task IpamCsvPreviewRequiresMappingAndCommitAddsOnlyPreviewedAddresses()
    {
        using var directory = new TemporaryTestDirectory();
        await using var factory = new TestWebApplicationFactory(directory.Path);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

        using var getImport = Request(HttpMethod.Get, "/Ipam/Import", "Administrator");
        using var importPage = await client.SendAsync(getImport);
        Assert.AreEqual(HttpStatusCode.OK, importPage.StatusCode);
        var html = await importPage.Content.ReadAsStringAsync();
        var tokenInput = Regex.Match(html, "<input[^>]*name=\"__RequestVerificationToken\"[^>]*>", RegexOptions.IgnoreCase);
        Assert.IsTrue(tokenInput.Success);
        var token = Regex.Match(tokenInput.Value, "value=\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;

        const string csv = "Address,Device,Assignment\r\n198.51.100.25,Inventory-only host,Reserved\r\n";
        var mapping = new Dictionary<string, string?> { ["IpAddress"] = "Address", ["DeviceName"] = "Device", ["AssignmentStatus"] = "Assignment" };
        var payload = JsonSerializer.Serialize(new { csv, mapping });
        using var previewRequest = JsonRequest(HttpMethod.Post, "/Ipam/PreviewImport", "Administrator", token, payload);
        using var previewResponse = await client.SendAsync(previewRequest);
        Assert.AreEqual(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.Content.ReadAsStringAsync();
        StringAssert.Contains(preview, "\"validCount\":1");
        StringAssert.Contains(preview, "\"ipAddress\":\"198.51.100.25\"");

        using var importRequest = JsonRequest(HttpMethod.Post, "/Ipam/CommitImport", "Administrator", token, payload);
        using var importResponse = await client.SendAsync(importRequest);
        Assert.AreEqual(HttpStatusCode.OK, importResponse.StatusCode);
        var report = await importResponse.Content.ReadAsStringAsync();
        StringAssert.Contains(report, "\"importedCount\":1");
    }

    [TestMethod]
    public async Task CsvImportValidatesRowsAndExportOmitsSnmpCredentials()
    {
        using var directory = new TemporaryTestDirectory();
        await using var factory = new TestWebApplicationFactory(directory.Path);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

        var services = factory.Services;
        var deviceService = services.GetRequiredService<IDeviceService>();
        var snmpDevice = await deviceService.CreateAsync(new DeviceEditInput
        {
            Name = "Secret device",
            IpAddress = "198.51.100.80",
            Type = DeviceType.Router,
            MonitoringMethod = MonitoringMethod.IcmpAndSnmp,
            SnmpVersion = SnmpVersion.V2c,
            SnmpCommunity = "export-must-not-contain-this"
        });
        Assert.AreEqual(DeviceWriteResult.Success, snmpDevice.Result);

        using var importPageRequest = Request(HttpMethod.Get, "/Devices/Import", "Administrator");
        using var importPage = await client.SendAsync(importPageRequest);
        Assert.AreEqual(HttpStatusCode.OK, importPage.StatusCode);
        var html = await importPage.Content.ReadAsStringAsync();
        var input = Regex.Match(html, "<input[^>]*name=\"__RequestVerificationToken\"[^>]*>", RegexOptions.IgnoreCase);
        Assert.IsTrue(input.Success, "The import form should render an antiforgery token.");
        var token = Regex.Match(input.Value, "value=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        Assert.IsTrue(token.Success, "The import form token should have a value.");

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(token.Groups[1].Value), "__RequestVerificationToken");
        var csv = "DeviceName,IpAddress,DeviceType,Location,Group,Description\r\nImported router,198.51.100.81,Router,Dhaka,Network,\"quoted, value\"\r\nDuplicate address,198.51.100.80,Router,Dhaka,Network,Existing\r\nInvalid row,not-an-ip,Router,Dhaka,Network,Invalid\r\n";
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "devices.csv");
        using var importRequest = Request(HttpMethod.Post, "/Devices/Import", "Administrator");
        importRequest.Content = form;
        using var importResponse = await client.SendAsync(importRequest);
        Assert.AreEqual(HttpStatusCode.OK, importResponse.StatusCode);
        var importResult = await importResponse.Content.ReadAsStringAsync();
        StringAssert.Contains(importResult, "Imported 1 device(s).");
        StringAssert.Contains(importResult, "198.51.100.80");
        StringAssert.Contains(importResult, "valid IP address");

        using var exportRequest = Request(HttpMethod.Get, "/Devices/Export", "ReadOnly");
        using var exportResponse = await client.SendAsync(exportRequest);
        Assert.AreEqual(HttpStatusCode.OK, exportResponse.StatusCode);
        Assert.AreEqual("text/csv", exportResponse.Content.Headers.ContentType?.MediaType);
        var exported = await exportResponse.Content.ReadAsStringAsync();
        StringAssert.Contains(exported, "Secret device");
        StringAssert.Contains(exported, "Imported router");
        Assert.IsFalse(exported.Contains("export-must-not-contain-this", StringComparison.Ordinal));
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? role)
    {
        var request = new HttpRequestMessage(method, path);
        if (role is not null)
        {
            request.Headers.Add("X-Test-Role", role);
        }

        return request;
    }

    private static HttpRequestMessage JsonRequest(HttpMethod method, string path, string? role, string antiforgeryToken, string json)
    {
        var request = Request(method, path, role);
        request.Headers.Add("RequestVerificationToken", antiforgeryToken);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }
}

internal sealed class TestWebApplicationFactory(string dataDirectory) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Storage:DataDirectory", dataDirectory);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                options.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
        });
    }
}

internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Phase2Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var roles = Request.Headers["X-Test-Role"].ToString()
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (roles.Length == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "phase2-test-user"),
            new(ClaimTypes.Name, "phase2-test-user")
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
