# AshPulse Infrastructure Monitor — Implementation Runbook

This runbook is a practical sequence for implementing the existing AshPulse application in a Windows Server/IIS environment. It does not claim that a target server, firewall rule, external service, or production data has already been validated. The detailed source-backed IIS instructions remain in the [Deployment guide](../DEPLOYMENT_GUIDE.md).

## 1. Implementation scope

The current application is an ASP.NET Core MVC/Razor application on .NET 10 with five solution projects. Data is stored in local JSON/JSONL files rather than a database server. The deployed web application hosts inventory, monitoring workers, notification delivery, and scheduled backups in one process. Use one IIS worker and a local persistent data path.

Implemented modules include local authentication and roles, device/server inventory, ICMP polling, optional read-only SNMP metrics, locations/groups, interactive topology maps, IPAM, reports, Telegram/SMTP notification configuration, theme administration, data integrity reporting, and Administrator-only backup/restore.

## 2. Plan the target environment

Before publishing, record:

- Host name, DNS, HTTPS certificate, IIS site/application pool, owner, support contact, and change record.
- Stable publish location and release/rollback location.
- Data directory and backup directory, free-space budget, ACL owner, off-host backup destination, and restore owner.
- Approved monitored device ranges and ICMP/SNMP egress policies.
- Notification destinations, credential owners, and external-provider test windows.
- User roles and administrator access policy, including MFA at an approved gateway (MFA is not built in).

Do not expose the application externally without approved network controls, trusted HTTPS, and an administrator access policy.

## 3. Prepare build and publish output

Use the .NET 10 SDK in the build environment. From the repository root, publish the Web project:

```powershell
dotnet publish src/UniversalITMonitor.Web/UniversalITMonitor.Web.csproj `
  -c Release -f net10.0 -r win-x64 --self-contained false `
  -o C:\Sites\AshPulse
```

Review the output and generated `web.config`. Do not remove or replace the generated ASP.NET Core Module configuration. Keep configuration and secrets out of general release artifacts.

## 4. Prepare IIS

1. Install the IIS Web Server role and matching x64 .NET 10 ASP.NET Core Hosting Bundle.
2. Create a dedicated application pool using **No Managed Code**, 64-bit mode, one worker, and **Load User Profile** enabled.
3. Configure AlwaysRunning/Application Initialization/preload and a shutdown timeout adequate for active polling to stop.
4. Bind a trusted HTTPS certificate to the intended host name and prevent unintended cleartext access.
5. Set `ASPNETCORE_ENVIRONMENT=Production` and `AllowedHosts` to the deployed host.
6. Allow only required outbound traffic: ICMP to approved monitored targets, UDP to configured SNMP targets/ports, and required HTTPS/SMTP destinations.

See the [IIS setup checklist](../IIS_SETUP_CHECKLIST.md) for target-server items.

## 5. Set storage and permissions

For a stable publish folder such as `C:\Sites\AshPulse`, the default layout is:

```text
C:\Sites\AshPulse\App_Data\   JSON/JSONL data, Data Protection keys, logs
C:\Sites\AshPulse\Backups\    backup ZIPs (created when used)
```

The application creates `App_Data` and its managed child directories when needed. With the default data location, grant the app-pool identity Modify access to the publish folder: the sidecar process lock, restore staging, data, logs, keys, and default backups require writes beneath that folder. `App_Data` is outside `wwwroot` and is not served as static content.

For versioned release directories, configure `Storage__DataDirectory` to a stable absolute path outside the release directory before first start. Configure the backup destination in the Administrator UI or through supported settings and grant the app-pool identity Modify to configured data/backup locations. Preserve data and Data Protection keys across upgrades.

The production Windows default uses DPAPI tied to the current app-pool identity/profile and its master-key context. A same-named app-pool identity on another server does not migrate that context. Plan certificate-based Data Protection key wrapping before protected credentials are created if cross-server migration is expected; follow the [Deployment guide](../DEPLOYMENT_GUIDE.md) and [disaster recovery guide](../DISASTER_RECOVERY_GUIDE.md).

## 6. First start and application setup

1. Start the site and inspect IIS/ANCM diagnostics and `<data directory>\logs` if it fails.
2. Confirm the intended Production host and data path. Confirm the app-pool identity can write where required.
3. Open the trusted HTTPS address and create the first Administrator. The username begins as `admin`; choose and securely store a password of at least 12 characters. There is no built-in preset password.
4. Create named user accounts and apply least privilege. Verify Read-only access separately.
5. Choose the organization theme and establish locations/groups before bulk inventory where practical.

First-run setup is a one-time empty-data-directory flow. After an administrator exists, users go to sign-in.

## 7. Configure monitoring and integrations

1. Set polling interval, timeout, and maximum concurrency based on server resources and network policies.
2. Add one approved test device and verify a real ICMP result from the application server.
3. Add optional SNMP details only for a device with approved read-only credentials. Verify its system/interface readings and note unsupported OIDs.
4. Configure Telegram and/or SMTP under **Notifications**. Use Test send and verify receipt at the destination before relying on delivery.
5. Import remaining device inventory using the current CSV template; review validation and duplicates before commit.
6. Register relevant subnets and import IPAM data. Link monitored devices where inventory should be authoritative.
7. Create a topology map linked to inventory device IDs and verify that status refreshes.

Live provider/agent compatibility cannot be inferred from code or a successful build. Use the [Production test checklist](../PRODUCTION_TEST_CHECKLIST.md) on the target environment.

## 8. Configure backup and restore

1. Select the backup directory, interval, and retention in **Backup and restore**.
2. Create a manual full backup and confirm its history entry, file size, and archive preview.
3. Copy an approved archive to protected off-host storage. ZIP backups are not encrypted containers; encrypt and restrict the off-host destination.
4. Perform a restore rehearsal in an isolated data directory before production cutover.
5. Record the archive version, restore result, operator, time, and recovery owner.

Do not delete safety archives through external cleanup rules without an approved retention procedure. Do not extract an archive over live data.

## 9. Go-live acceptance and handover

Complete and attach the [IIS setup checklist](../IIS_SETUP_CHECKLIST.md) and [Production test checklist](../PRODUCTION_TEST_CHECKLIST.md). Record at minimum:

- [ ] Build/release identifier, publish path, configuration owner, and rollback path.
- [ ] HTTPS binding and successful login/logout.
- [ ] Administrator, Operator, and Read-only route/operation checks.
- [ ] Real ICMP success and controlled failure/recovery behavior.
- [ ] SNMP check against the actual supported agent, if enabled.
- [ ] Telegram/SMTP test-send receipt, if enabled.
- [ ] Dashboard/report values correspond to persisted records; unavailable history is understood.
- [ ] Manual backup, preview, isolated restore rehearsal, ACLs, and off-host archive controls.
- [ ] IIS recycle/start behavior, one-worker configuration, monitoring resumption, disk/ACL review.
- [ ] Owner, alert escalation contacts, on-call procedure, restore authority, and review cadence.

An unchecked item is not verified. Do not record external monitoring, notification, restore, or IIS behavior as passed without target-environment evidence.

## 10. Upgrade and rollback sequence

1. Create and validate a fresh full backup. Record current release/configuration and confirm free space.
2. Stop/offline the old instance and wait for the worker to exit and release its data-directory lock.
3. Publish to a new release folder or update the stable site folder while preserving the configured data and backup directories.
4. Carry forward approved Production configuration and effective ACLs.
5. Start the new release and confirm startup logs, sign-in, roles, inventory, polling, notification status, and backup history.
6. Retain the previous release and pre-upgrade archive until sign-off.
7. If rollback is required, stop the new release and point IIS to the prior release. If application data changed, assess compatibility and use the validated restore flow; a binary rollback alone does not roll data back.

## 11. Operations and known constraints

- One local JSON/JSONL instance is supported; this is not a distributed database or multi-node shared-store design.
- Monitoring history/sample files are append-only without built-in compaction/retention. Monitor disk use; do not truncate active files externally.
- Historical availability uses persisted transitions; absent history remains unavailable/unverified. No missing response samples are estimated.
- Notification delivery is external and may be at-least-once in a narrow crash window. Monitor delivery history and maintain provider escalation paths.
- The app has no built-in MFA, Active Directory, server-side PDF renderer, or Excel IPAM support.
- The source does not configure trusted reverse-proxy forwarded headers. Use direct IIS TLS termination unless proxy support is implemented and validated.

## 12. Sign-off record

| Field | Target value |
| --- | --- |
| Environment / site URL | |
| Release version | |
| Publish / data / backup paths | |
| App-pool identity | |
| Administrator / support owner | |
| Alert escalation owner | |
| Latest verified backup and restore | |
| Outstanding deviations | |
| Operator and approval date | |
