# Universal IT Monitor deployment guide

This guide is a deployment procedure for the existing ASP.NET Core MVC application. It describes configuration and checks to perform; it does not certify a particular Windows Server, IIS, certificate, network, backup, or provider configuration. The repository's automated tests do not replace the production checks in [PRODUCTION_TEST_CHECKLIST.md](./PRODUCTION_TEST_CHECKLIST.md).

## Runtime and IIS prerequisites

The Web project targets `net10.0`. For the framework-dependent IIS deployment below, install the **x64 .NET 10 ASP.NET Core Hosting Bundle** on the IIS server. It supplies the .NET 10 runtime, ASP.NET Core runtime, and ASP.NET Core Module (ANCM) for IIS. Install IIS first, then install or repair the Hosting Bundle, and restart IIS services (or the server) if the installer requests it. The latest servicing bundle must be installed at deployment time; the .NET 10 download page listed ASP.NET Core Runtime 10.0.14 on 2026-11-01. Confirm the currently supported .NET 10 servicing version before release rather than pinning an old patch. The server does not need the SDK for this framework-dependent deployment.

- [Microsoft: Host ASP.NET Core on Windows with IIS](https://learn.microsoft.com/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0)
- [Microsoft: Publish an ASP.NET Core app to IIS](https://learn.microsoft.com/aspnet/core/tutorials/publish-to-iis?view=aspnetcore-10.0)
- [Microsoft: .NET 10 downloads](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Microsoft: Supported .NET versions on Windows](https://learn.microsoft.com/dotnet/core/install/windows)

Microsoft's ASP.NET Core/IIS guidance lists Windows Server 2012 R2 or later as a hosting platform. The current .NET-on-Windows runtime table lists .NET 10 x64 for Windows Server 2025, 23H2, 2022, 2019, 2016, 2012 R2, and 2012, but runtime compatibility does not extend the Windows Server product lifecycle. Select a Windows Server release that remains supported by Microsoft and the organization's security policy (for example, a currently supported Server 2022/2025 deployment); do not select an out-of-support OS solely because the runtime can start on it.

Install the IIS Web Server role and the Static Content, Default Document, HTTP Errors, Request Filtering, and IIS Management Console features needed by the site. ANCM comes from the Hosting Bundle; this is not an ASP.NET Framework application and does not require installing the ASP.NET 4.x feature set for the app.

## Build, test, and publish

Run from a clean, reviewed source checkout on a build machine with the .NET 10 SDK. Use the same release commit for the test and publish steps.

```powershell
$ErrorActionPreference = 'Stop'
dotnet --info
dotnet restore .\UniversalITMonitor.sln
dotnet build .\UniversalITMonitor.sln -c Release --no-restore
dotnet test .\UniversalITMonitor.sln -c Release --no-build --no-restore

$release = Get-Date -Format 'yyyyMMdd-HHmmss'
$publishPath = "C:\UIM-Staging\UniversalITMonitor\$release"
New-Item -ItemType Directory -Force -Path $publishPath | Out-Null
dotnet publish .\src\UniversalITMonitor.Web\UniversalITMonitor.Web.csproj `
  -c Release -f net10.0 -r win-x64 --self-contained false -o $publishPath
```

Review the publish output, generated `web.config`, and release file hash/manifest before copying it to the server. `dotnet publish` generates the ANCM configuration; preserve the generated `web.config`. Do not set `stdoutLogEnabled="true"` as routine logging: the application has its own redacted, rotated log files, while ANCM stdout logs require separate ACL and cleanup controls. With the in-site default data path, the application identity needs Modify on the publish folder for its data, lock, and restore staging files. Use an external stable data path to keep application binaries read-only. Do not publish developer secrets or copy developer runtime data into Production.

## Production configuration and storage layout

The default keeps application data in `App_Data` beneath the publish directory. Example:

```text
C:\Sites\UniversalITMonitor\                    published application
C:\Sites\UniversalITMonitor\App_Data\          live JSON, JSONL, Data Protection keys, logs
C:\Sites\UniversalITMonitor\Backups\           verified backup ZIPs and safety archives
```

On startup, the app creates `App_Data`, its JSON files, `keys`, and `logs`; backup operations create `Backups` when needed. The process lock and full-restore staging folders are created beside `App_Data`, in the publish folder. Grant the app-pool identity Modify on the publish folder when using the default path. `App_Data` is outside `wwwroot`, so its files are not served as static content. Keep the site at a stable physical path and preserve `App_Data` and `Backups` when copying a new build over it. If each release uses a new directory, configure an absolute stable `Storage:DataDirectory` outside that release directory instead.

Set `ASPNETCORE_ENVIRONMENT=Production` for the IIS application. Create an `appsettings.Production.json` beside the published files to set the actual host name. The default data path needs no Production override. Example:

```json
{
  "AllowedHosts": "uim.example.internal",
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    },
    "RollingFile": {
      "MaximumFileBytes": 10485760,
      "RetainedFiles": 30
    }
  },
  "DataProtection": {
    "CertificateThumbprint": "REPLACE_WITH_LOCALMACHINE_CERTIFICATE_THUMBPRINT"
  }
}
```

This configuration contains no credentials. The default Data Protection key ring is `<publish>\App_Data\keys` and redacted application logs are under `<publish>\App_Data\logs`; neither has a separate path setting. `Logging:RollingFile` configures size and file retention, not a separate log directory. Backup storage defaults to `<publish>\Backups` and is saved in `backup-settings.json`; an Administrator can select a separate absolute location in **Backup and restore**. Keep backups separate from the data directory and `wwwroot`. To use a stable data path across versioned release folders, set `Storage:DataDirectory` to an absolute path outside the release directory.

Do not leave `AllowedHosts` as `*` in Production. Open the site over trusted HTTPS and create the first administrator on the setup page. The username starts as `admin`, and you choose the password. The first account is created only once; after it is saved, setup redirects to sign-in.

## Data Protection certificate

The application stores its key ring beneath the data directory. Production on Windows uses current-user DPAPI if `DataProtection:CertificateThumbprint` is absent. For a planned cross-server migration, configure a dedicated **Data Protection key-encryption certificate** in `LocalMachine\My` before protected credentials are created. The app looks it up by thumbprint and requires an accessible private key. Grant the app-pool identity Read access to that private key. Keep the certificate's PFX/private-key backup encrypted and access-controlled outside the app ZIPs. The HTTPS site certificate and Data Protection key-encryption certificate serve different purposes; use separate certificates and ACLs.

If the key-encryption certificate is missing or its private key cannot be opened, startup or credential restoration can fail. Changing the thumbprint does not rewrap existing keys. Keep every previous key-encryption certificate needed by the persisted key ring until a reviewed key-ring rotation/migration has been completed. The application does not migrate source-machine DPAPI master keys. See [DISASTER_RECOVERY_GUIDE.md](./DISASTER_RECOVERY_GUIDE.md) for migration rules.

## IIS site and identity

Create one dedicated application pool, for example `UniversalITMonitorPool`:

- .NET CLR: **No Managed Code**; pipeline: Integrated.
- Enable 32-bit applications: **False** for the x64 publish.
- Identity: `ApplicationPoolIdentity` (or a dedicated gMSA/service identity if required by local policy); never use LocalSystem or an administrator account.
- Load User Profile: **True**, because the default Windows DPAPI key protection is current-user scoped.
- Maximum Worker Processes: **1**. The exclusive data-directory lease serializes app processes, but this app is not a multi-worker/web-garden deployment.
- Start Mode: **AlwaysRunning**; Idle Time-out: **0**; install/enable IIS Application Initialization and set the site/application `Preload Enabled` property. Monitoring and automatic backups are hosted background workers and require the application to stay running.
- Keep the normal recycle schedule and shutdown limits under change control. Validate overlapped recycle and stop behavior in staging before production. Permit enough shutdown time for a polling cycle and data lease to close.

Create an IIS site or application with the publish directory as its physical path. Use a dedicated host name and HTTPS binding. The application-pool identity needs Modify on the publish directory when using the in-site default data path, because its data, process lock, restore staging, and default backups are all beneath that folder. If external data and backup paths are configured, grant Modify on those locations and Read & Execute on the publish directory instead. Apply inherited permissions to child files; grant no interactive logon rights. Example ACL commands from an elevated PowerShell prompt (adjust paths and pool name first):

```powershell
icacls 'C:\Sites\UniversalITMonitor' /grant 'IIS AppPool\UniversalITMonitorPool:(OI)(CI)M'
```

Check existing inherited ACLs before applying grants; do not use `/reset` or broad `Everyone`/`Users` Modify grants. Keep deployment operators separate from the app-pool identity. Restrict backup ZIPs and logs: backup archives contain readable inventory/settings even though protected credential values remain encrypted.

## HTTPS and reverse proxies

Install a valid TLS server certificate with the public or internal DNS name in its SAN into `Local Computer\Personal` and bind it to HTTPS/443 in IIS. Use SNI when sharing an address. Ensure clients trust the issuing CA, redirect/disable cleartext access at the edge, renew the certificate before expiry, and test the complete chain from a client. Do not grant the app pool access to the TLS private key unless a separately justified feature requires it.

This checkout does not configure `ForwardedHeadersOptions` or call `UseForwardedHeaders`. Deploy directly on IIS with TLS termination at IIS unless a reverse-proxy configuration has been added and validated in code. If an upstream proxy is required, it must remove client-supplied forwarded headers, set its own forwarded values, and be the only network path to the site. Configure the application to trust the exact proxy IP/CIDR and exact hop count, and call forwarded-header middleware before HSTS and HTTPS redirection. Do not set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` as a shortcut: it clears proxy trust restrictions. Verify scheme, host, remote IP, redirects, secure cookies, and IP rate limiting in an isolated environment. See [Microsoft forwarded-header guidance](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).

If reverse-proxy support is approved, the source change should follow this shape, replacing the example address with the actual last-hop proxy and adding `UseForwardedHeaders` before `UseHsts`/`UseHttpsRedirection`. This snippet is **not present in the current build** and is not an appsettings switch:

```csharp
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Add(IPAddress.Parse("10.20.30.40"));
    options.ForwardLimit = 1;
});

// After exception handling and before HSTS / HTTPS redirection:
app.UseForwardedHeaders();
```

## Initial start and operational checks

1. Confirm IIS and ANCM recognize the application. Start the pool and request the HTTPS site. If startup fails, inspect Windows Application Event Log/ANCM diagnostics and `<publish>\App_Data\logs`; enable ANCM stdout logging only temporarily with a protected folder, then disable and remove those diagnostic files.
2. Check the app starts with the intended `Production` environment, `App_Data`, certificate thumbprint (if used), data/keys/logs directories, and no writes under `wwwroot`.
3. Open the site over trusted HTTPS and create the first Admin from the setup page. Add a Read-only test account and verify denied Admin routes.
4. Confirm the default backup path is `Backups` beside the publish directory or select a separate absolute path in **Backup and restore**. Create and download a manual backup; preview/validate it. Enable a schedule and retention after validating a test backup.
5. Configure monitoring and one controlled test device. Verify actual ICMP status/history, then test notification channels and SNMP only against approved lab/production endpoints.
6. Complete every applicable unchecked step in [IIS_SETUP_CHECKLIST.md](./IIS_SETUP_CHECKLIST.md) and [PRODUCTION_TEST_CHECKLIST.md](./PRODUCTION_TEST_CHECKLIST.md). Record build/version, operator, timestamp, evidence, and deviations.

## Upgrade and rollback

See [IIS_SETUP_CHECKLIST.md](./IIS_SETUP_CHECKLIST.md) for the detailed sequence. Briefly: verify a recent backup and its manifest; stop or offline the app and wait for the old worker to exit/release the sidecar lease; preserve `App_Data` and `Backups` when deploying to the same site path, or use stable absolute data and backup paths for versioned releases; publish and copy the new files; carry forward the Production config and ACLs; point IIS at the release; start it; verify logs, monitoring, backup, and Admin/Read-only access. Keep the previous release and pre-upgrade backup until checks pass.

If the new release fails, stop it and point IIS back to the previous release. Preserve the current data directory and logs. If the new app already changed JSON, assess compatibility and use the app's validated restore flow from a pre-upgrade archive rather than assuming binary rollback restores data. Full restore creates a safety archive, swaps staged data with rollback handling, and stops the application so it can restart with fresh repositories and keys. Never manually extract an unvalidated ZIP over live data.

## Recovery, integrations, and capacity

- Use [DISASTER_RECOVERY_GUIDE.md](./DISASTER_RECOVERY_GUIDE.md) for archive restore, key recovery, and cross-server migration.
- Use [PRODUCTION_TEST_CHECKLIST.md](./PRODUCTION_TEST_CHECKLIST.md) for live Telegram, SMTP, SNMP, responsive-layout, backup, scheduled-worker, and IIS recycle procedures. These are operator-run checks; no live integration result is implied here.
- Monitoring history and samples are append-only JSONL without a built-in retention/compaction implementation. Do not truncate, rename, or rotate active per-device files externally: reports expect the original paths and complete data. Follow the safe interim and future archival recommendations in the production test checklist and status file.

## Deployment references

- [Microsoft IIS hosting guidance for ASP.NET Core 10](https://learn.microsoft.com/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0)
- [Microsoft ASP.NET Core Data Protection configuration](https://learn.microsoft.com/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0)
- [Microsoft ASP.NET Core Data Protection key storage providers](https://learn.microsoft.com/aspnet/core/security/data-protection/implementation/key-storage-providers?view=aspnetcore-10.0)
