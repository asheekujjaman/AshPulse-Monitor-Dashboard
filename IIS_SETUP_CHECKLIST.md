# Windows Server and IIS setup checklist

Use this checklist with [DEPLOYMENT_GUIDE.md](./DEPLOYMENT_GUIDE.md). Mark each item only after an operator has performed it on the target environment and retained evidence. Blank items are not completed tests.

## Change record

- Server / environment: ______________________________
- IIS site and application pool: _____________________
- Release commit / version: _________________________
- Operator / change ticket: _________________________
- Date and time (UTC): ______________________________
- Approved rollback owner: __________________________

## Server prerequisites

- [ ] Confirm Windows Server version, patch baseline, hostname, DNS, time synchronization, disk volumes, and backup storage ownership.
- [ ] Install IIS Web Server role with Static Content, Default Document, HTTP Errors, Request Filtering, and IIS Management Console as required.
- [ ] Install the x64 .NET 10 ASP.NET Core Hosting Bundle **after IIS**. Check the current supported .NET 10 servicing release on the official download page on deployment day; do not use an out-of-support runtime. On 2026-11-01, the page listed ASP.NET Core Runtime 10.0.14.
- [ ] If IIS was installed after the bundle, repair/re-run the Hosting Bundle installer.
- [ ] Restart IIS services or reboot if the installer requests it; verify ANCM is registered and record runtime/module versions.
- [ ] Confirm outbound ICMP to approved monitored targets; UDP/161 to configured SNMP agents; and only the required HTTPS/SMTP relay ports. Document firewall rules and source/destination scopes.
- [ ] Confirm the app host has enough free space for JSON writes, logs, current data, staging/rollback siblings, and backups. Backups are not encrypted ZIP containers; apply storage encryption/access controls outside the application.

## Paths and NTFS ACLs

Use dedicated paths on supported local NTFS volumes. Example:

```text
Publish: C:\Sites\UniversalITMonitor
Data:    C:\Sites\UniversalITMonitor\App_Data
Keys:    C:\Sites\UniversalITMonitor\App_Data\keys       (application-managed)
Logs:    C:\Sites\UniversalITMonitor\App_Data\logs       (application-managed)
Backups: C:\Sites\UniversalITMonitor\Backups
```

- [ ] Confirm the app pool can create and write `App_Data` under the publish folder. The app creates it, its `keys` and `logs` subfolders, JSON files, and the default `Backups` folder as needed. `App_Data` is outside `wwwroot`.
- [ ] Grant the application-pool identity Modify on the publish folder when using the default in-site data path. Restore staging and the process lock are siblings of `App_Data`.
- [ ] If using an external `Storage:DataDirectory` or backup location, grant Modify on the dedicated parent and children for that path.
- [ ] Keep `App_Data` and `Backups` when replacing the app in the same IIS folder. For versioned release folders, use stable external data and backup locations or copy the live data before startup.
- [ ] Review effective ACLs and inheritance. Remove broad write access; do not grant `Everyone` or `Users` Modify. The app pool needs publish-folder Modify with the default path; use external paths to keep the release tree read-only.
- [ ] Confirm backup files/logs are readable only by the app identity, Administrators, and required backup operators.
- [ ] Confirm antivirus/EDR exclusions, if any, are narrowly scoped and approved. Do not disable protection for the entire data volume.

## Certificates and configuration

- [ ] Install a currently valid server-auth TLS certificate with the served DNS name in its SAN in `Local Computer\Personal`.
- [ ] Bind HTTPS/443 in IIS; configure SNI/host name for shared endpoints. Test client trust chain and renewal monitoring.
- [ ] For cross-server Data Protection, install the separate key-encryption certificate with private key into `LocalMachine\My`; record thumbprint and expiry. Grant the pool identity Read on its private key.
- [ ] If staying on one server with DPAPI protection, confirm the same pool identity/profile and `keys` directory are preserved across restart and restore.
- [ ] Create a Production config with the exact host, rolling log size/retention, and (if configured) the Data Protection certificate thumbprint. The default data path requires no Production override.
- [ ] Configure `ASPNETCORE_ENVIRONMENT=Production` at the app process/site. Confirm no conflicting duplicate environment values.
- [ ] Complete first administrator setup over trusted HTTPS before making the site generally available. Setup is available only until the first account is created.
- [ ] Confirm no real secrets are committed, left in staging, or copied into general release artifacts. Provider credentials are entered through the protected Admin UI.
- [ ] Set `AllowedHosts` to the actual host name; do not retain the wildcard in Production.

## Application pool and IIS site

- [ ] Create a dedicated application pool. Set **.NET CLR Version: No Managed Code**, Integrated pipeline, and **Enable 32-Bit Applications: False** for x64 publishing.
- [ ] Select `ApplicationPoolIdentity` or an approved dedicated service identity; do not use LocalSystem or an administrator account.
- [ ] Set **Load User Profile: True** for current-user DPAPI compatibility.
- [ ] Set **Maximum Worker Processes: 1**. This is a single-instance JSON application, not a web garden or multi-node deployment.
- [ ] Set **Start Mode: AlwaysRunning** and **Idle Time-out: 0**. Install/enable Application Initialization and set site/application **Preload Enabled** so monitoring and scheduled backups start without waiting for a manual visit.
- [ ] Review periodic recycle and shutdown limits. The process lease serializes overlapping workers, but actual recycle timing must be tested; allow enough time for the old worker to stop.
- [ ] Set the site physical path to the folder containing the published `web.config`; preserve that file. Keep ANCM stdout logging disabled except for time-limited troubleshooting.
- [ ] Configure the production HTTPS binding and redirect policy. Verify the site is not anonymously exposed over an unintended HTTP binding.
- [ ] If an upstream reverse proxy is used, stop here until trusted forwarded-header handling has been added to the app and tested. Current source does not configure `UseForwardedHeaders`/`KnownProxies`; do not trust arbitrary `X-Forwarded-*` headers or enable an unrestricted forwarded-header environment switch.
- [ ] Confirm application-pool identity is not granted interactive logon, Remote Desktop, or unrelated local groups.

## First start and protected access

- [ ] Start the pool/site and verify the app starts in Production. Check Windows Application Event Log, ANCM startup messages, and `<data>\logs` for safe startup diagnostics.
- [ ] Verify application data is created under `App_Data`, with keys and logs beneath it. Verify there are no writes under `wwwroot`.
- [ ] Complete first-run Admin account creation from a trusted HTTPS client. The username starts as `admin`; set a password of at least 12 characters.
- [ ] Require administrator MFA through an approved access layer; this application has no built-in MFA.
- [ ] Create a temporary Read-only account and validate read vs Admin-only navigation/routes. Remove or disable the test account afterward.
- [ ] In **Backup and restore**, confirm the default `Backups` folder beside the publish directory or set a separate absolute backup directory. Set interval/retention, create a manual backup, preview it, and verify the history row and file size.
- [ ] Confirm backup destination ACLs, off-host copy/encryption policy, and restore owner. Record the date of the latest verified restore rehearsal.
- [ ] Configure one approved device and validate current status, last check, response time, transition history, and recovered status. Do not use a live network target without authorization.

## Change, rollback, and operational sign-off

- [ ] Before each upgrade, create a new verified full backup, record current release/configuration, and check available disk space.
- [ ] Publish the new files and preserve the live `App_Data` folder. If switching to a new release directory, set a stable external `Storage:DataDirectory` or copy the current data before startup.
- [ ] Stop/offline the site and application pool and wait until the old worker exits and its data-directory lease is released before copying or swapping data.
- [ ] Keep previous release, pre-upgrade backup, current data snapshot, and Data Protection key-encryption certificates until post-upgrade verification is signed off.
- [ ] On failure, stop the new release and switch the IIS physical path back. If the new code changed data, use a validated restore or reviewed compatibility procedure; a binary rollback alone does not roll data back.
- [ ] Check automatic backup history after a due schedule, check file retention, and confirm logs rotate and remain free of credentials.
- [ ] Perform an IIS recycle exercise in staging, verify requests recover, only one worker owns the data lease, monitoring resumes, and scheduled backup timing remains valid.
- [ ] Attach completed [PRODUCTION_TEST_CHECKLIST.md](./PRODUCTION_TEST_CHECKLIST.md) and record outstanding risks/approvals.

## Target-specific values

| Setting | Approved value |
| --- | --- |
| Public/internal DNS name | ______________________________ |
| HTTPS certificate thumbprint / expiry | ______________________________ |
| Data Protection certificate thumbprint / expiry | ______________________________ |
| `Storage:DataDirectory` | ______________________________ |
| Backup directory | ______________________________ |
| IIS pool identity | ______________________________ |
| App pool recycle/shutdown policy | ______________________________ |
| Proxy IP/CIDR and hop count, if applicable | ______________________________ |
| Alert recipient / escalation owner | ______________________________ |
