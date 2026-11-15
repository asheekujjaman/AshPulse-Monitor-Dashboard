# AshPulse Infrastructure Monitor

AshPulse Infrastructure Monitor is an on-premises ASP.NET Core MVC application for network device inventory and monitoring. It stores application data locally as JSON and uses no database server or cloud service.

## AshPulse documentation

Start at the [AshPulse documentation index](./docs/ASHPULSE_DOCUMENTATION_INDEX.md) for the user guide, administrator guide, implementation runbook, training guide, architecture references, and IIS/operations checklists.

## Current phase

Production hardening preserves Phases 1–5 and adds durable monitoring transition recovery, cross-file reference reconciliation, bounded notification retries, automatic App_Data storage, exclusive single-instance operation, rotated redacted logging, persistent colour themes, and monitoring favicon assets. See [PROJECT_STATUS.md](./PROJECT_STATUS.md) for current verification and limits, and [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md) for storage and recovery details.

## Requirements

- .NET 10 SDK to build and run.
- Windows Server with the matching ASP.NET Core Hosting Bundle for IIS deployment.
- ICMP access from the application server to monitored devices. SNMP metrics require the configured device to allow read-only UDP requests on its SNMP port.

## Run locally

From the repository root:

```powershell
dotnet run --project src/UniversalITMonitor.Web/UniversalITMonitor.Web.csproj
```

Open the local URL printed by `dotnet run`. First-run setup creates the initial administrator and closes after the account is saved. The form starts with username `admin`; choose a password of at least 12 characters. When an administrator already exists, the app redirects to sign-in.

JSON data defaults to `<application folder>/App_Data`, outside `wwwroot`, and is created automatically at startup. Logs and Data Protection keys are created beneath that directory. Backups default to the sibling `<application folder>/Backups` and are created when a backup is made. With this in-site default, the IIS application identity needs Modify access to the site folder because restore staging and the process lock are created beside `App_Data`. For versioned release folders, either preserve `App_Data` when upgrading or set `Storage__DataDirectory` to a stable absolute path outside the published site.

## Device monitoring

- Add devices with a name and IPv4 or IPv6 address. Assign them to stable-ID locations/buildings and nested groups; the original free-text assignments are migrated at startup without clearing device state or monitoring history.
- The Devices inventory shows the live total and UP, DOWN, and UNKNOWN counts. Administrators and Operators can discover ICMP-responsive hosts on IPv4 subnets from /22 through /32, review IP addresses and suggested hostnames, then add one device or a multi-selection with type, monitoring, location/building/group, description, and optional SNMP settings.
- ICMP checks are enabled by default. The administrator can change the poll interval, ping timeout, and maximum concurrent checks under **Monitoring settings**.
- SNMP is optional. SNMP v2c community strings and SNMP v3 credentials are encrypted in local storage using ASP.NET Core Data Protection. The UI never renders the secret values; leaving an edit field blank keeps the saved secret. CSV exports do not include SNMP credentials.
- SNMP supports standard system/interface OIDs, 64-bit counters with 32-bit IF-MIB fallback, and assigned VLAN lookup through Cisco VLAN membership or BRIDGE/Q-BRIDGE PVID tables when exposed. CPU and memory are shown when supported. Device Details includes a live SNMP test and per-browser hide/show controls for interfaces.
- CSV imports require `DeviceName` (or `Name`) and `IpAddress`; download the template from **Import devices**. Imported records start with ICMP monitoring. SNMP configuration can be added in the device editor.
- Device checks, current status, and pending status-transition events are committed together. A durable outbox is replayed idempotently to history and notifications after process restart. Status transitions and check samples remain append-only per-device JSONL files. Latest SNMP metrics use separate per-device JSON files.

Administrators and Operators can create, edit, import, delete, and enable or pause monitoring. Read-only users can view, search, filter, and export inventory.

## Locations, groups, and network maps

- Create nested sites, buildings, areas, and operational groups. Location/group pages show devices below a node with current status counts. Device assignments can be moved through the device editor.
- Deleting a location or group with assigned devices requires reassignment or an explicit unassignment confirmation. Child nodes must be moved first.
- Create multiple topology maps scoped to locations/buildings. Drag existing inventory devices onto the canvas, select one of the 11 icon styles, move/resize icons, and connect devices. Maps persist in local JSON and can be exported/imported as JSON.
- Imported maps are previewed against current device IDs. Missing references are listed and must be resolved before import. Device status is read from monitoring data and refreshes while the editor is open.
- Deleting a device removes its map nodes/connections. Map nodes never create separate device records.

## IP address management

- IPAM is a separate inventory module for IP records and IPv4/IPv6 CIDR subnet registration. Record fields cover VLAN, device, location/building/group, switch/port, MAC, assignee, department, description, and an explicit assignment status.
- Administrators can discover ICMP-responsive hosts on registered IPv4 subnets from /22 through /32, review results, and add one or multiple IP records with shared inventory details. Existing monitored devices are linked automatically; reachability does not determine ownership or assignment status.
- IP addresses are normalized and unique within IPAM. Subnet membership is validated, and a subnet cannot be deleted while records reference it.
- Link a record to an existing monitored device to keep its name, IP, type, location, and group in sync. An ICMP response alone never determines the address owner or assignment status.
- Administrators can create/edit/delete records and subnets. CSV import supports column mapping, preview, validation, duplicate reporting, and a commit step that skips duplicates without overwriting existing entries. CSV export and a downloadable template are available.
- Excel import/export is not included; this version uses CSV and JSON formats without a spreadsheet dependency.

## Notifications

- Administrators configure Telegram Bot API and SMTP email delivery, enable either channel, and send test messages from **Notifications**.
- Telegram bot tokens and SMTP usernames/passwords are encrypted with ASP.NET Core Data Protection in the local JSON settings file. The UI exposes only whether a credential is saved; delivery logs and exports never include credentials.
- The monitor queues alerts on a transition to DOWN and a recovery from DOWN to UP. Repeated DOWN checks do not create repeat alerts. The durable notification outbox runs independently from monitoring and records per-channel delivery results.
- Transient Telegram/SMTP failures retry with bounded backoff for up to five attempts; permanent failures and exhausted retries appear as dead letters. Administrators can retry dead-lettered channels. A process crash after a provider accepts a message but before its result is saved can cause an at-least-once duplicate.
- Telegram and SMTP sending code is implemented, but live provider delivery has not been tested against real provider credentials or servers. Configure and use the test-send action in the target environment before relying on delivery.

## User administration

- Administrators can create, edit, disable, and delete local accounts with Administrator, Operator (retained for compatibility), and Read-only roles. Passwords use ASP.NET Core Identity password hashing; an administrator can reset a user's password through the edit form.
- Login throttling uses a 10-request per-minute IP limit and a persistent account lockout after five failed password attempts for 15 minutes. There is no built-in MFA; require an MFA-protected access gateway or equivalent administrator access controls before external exposure.
- User-management actions require the Administrator policy on the server. The current local administrator cannot be disabled, demoted, or deleted if that would leave no active administrator. Existing cookies are revalidated against current account state and roles on each request.
- Active Directory settings and authentication are not present in this checkout. Local accounts remain the supported sign-in path; no AD interoperability is claimed.

## Backup, restore, and migration

- Administrators can create a full ZIP backup from **Backup and restore** or configure automatic backups, interval, retention, and an absolute backup directory. The default directory is `Backups` beside the configured data directory. Backup storage remains separate from the data directory and the web root.
- Archives contain the JSON documents, history/sample JSONL files, SNMP metric snapshots, and Data Protection key ring. Each archive includes an application/format version, UTC creation time, file list, sizes, and SHA-256 hashes. ZIP paths and manifests are validated before data is staged or replaced.
- Passwords are stored as ASP.NET Identity hashes. SNMP, Telegram, and SMTP credentials remain Data Protection ciphertext in the archive; plaintext values are never included. The key ring is included because those credentials cannot be decrypted without the matching keys and protection context. Protect the backup directory and exported ZIPs with Windows ACLs and encrypted off-server storage.
- Restore preview validates the archive and its data before showing the file list and available modules. Full restore and selected-module restore both require explicit replacement confirmation and first create a verified safety backup. Unsafe cross-module references or incompatible credentials block restore. A full restore swaps the data directory and restarts the application; selected modules leave unrelated files in place.
- To migrate, publish/copy the compatible application files and configuration, stop the source and target instances, then preserve or restore `App_Data` and `Backups` with the application. For versioned release folders, set `Storage__DataDirectory` to a stable absolute path. Grant the IIS application-pool identity Modify access to the site folder when using the default, or to the configured external data/backup locations.
- Production Windows defaults to Data Protection keys wrapped with DPAPI for the current application-pool identity. Copying the `keys` folder or creating a same-named app-pool identity on a different server is not enough to decrypt keys protected by the source server's DPAPI context. Cross-server migration of those older credentials requires a supported migration of the protecting Windows identity/profile and DPAPI master-key context; this application does not migrate DPAPI master keys.
- For a planned cross-server deployment, configure a certificate with a private key in `LocalMachine\My` before protected credentials are created. Set `DataProtection__CertificateThumbprint` to its thumbprint and grant the application-pool identity access to the private key. Install that same certificate/private key on the destination and retain the same Data Protection application name and key ring. Enabling a certificate later does not rewrap old DPAPI-protected keys. The restore validator refuses archives whose protected credentials cannot be opened; it does not silently discard ciphertext.
- Backups are local/offline ZIP files, not encrypted archive containers. Data Protection protects the sensitive credential payloads inside them, but file names and other inventory/report data remain readable to anyone who can open the ZIP. Use protected storage and restricted permissions.

## Reports

- Authenticated users can view device availability, persisted status transitions, response-time checks, current location/group status summaries, and current IPAM inventory.
- Date ranges use UTC and are limited to 366 days. Device, location, nested group, current device status, and IPAM assignment status filters are supported. Availability is calculated from recorded status transitions; intervals without a known state are shown as Unknown and excluded from the availability percentage. Missing history is identified in the report rather than estimated.
- Response statistics and history use actual monitoring samples persisted since Phase 4 was introduced. Earlier response-time data is unavailable. Location/group summaries and IPAM data are current snapshots, not historical snapshots.
- CSV exports use consistent columns and neutralize spreadsheet formula prefixes. Report pages support browser printing and the browser's Save as PDF option; a server-side PDF generator is not included.

## Dashboard and global search

The dashboard includes real device status, location/group health summaries, and IPAM assignment totals. Global search covers devices, IPAM records, subnets, locations, groups, and maps; device results include the current monitoring status, response time, and last check.

## Production hardening, themes, and branding

- Startup reconciles stale device, hierarchy, map, and IPAM references and records the results in the Admin-only **Data integrity** page. Repairable stale IDs are cleared or uniquely reattached while saved inventory text and map nodes are retained. Duplicate stable IDs are separated with a review entry.
- Missing primary JSON files recover atomically from valid `.bak` copies; invalid backups remain preserved and stop initialization rather than silently returning an empty document.
- Application logs are written as redacted JSONL under `<data directory>/logs`, flushed per event, rotated at 10 MB, and retained for 30 files by default. Log storage failures are swallowed so a disk/ACL problem does not interrupt monitoring; verify the configured data-directory ACL and disk space.
- An exclusive lock beside the data directory prevents overlapping app processes from reading/writing the same JSON store. During IIS overlapped recycle, the new process waits for the old process to release its lock. Keep one IIS worker process and avoid a web garden.
- Admins can set the application default to Ocean Blue, Midnight Purple, or Emerald Green in **Appearance**. Themes survive refresh and sign-out; custom product/organization branding is preserved. Device status colors remain green/red/yellow/gray across themes.
- A low-opacity network pattern and SVG/PNG/ICO/Apple touch favicon assets use the same monitoring shield identity across the browser tab, sign-in page, and application. The PWA manifest uses relative icon and scope paths so it also works under an IIS virtual application. CSV imports cap files, fields, columns, and rows; map JSON caps payloads, nodes, and connections; exports return HTTP 413 instead of silently truncating oversized data.
- The reconciliation report and logs are operational data, not extra device records. The application remains JSON/JSONL-only; no database or network configuration agent is introduced.

## Testing

Run the automated regression suite from the repository root:

```powershell
dotnet test UniversalITMonitor.sln
```

The suite covers Admin/Read-only authorization, all three themes across application pages, favicon assets, Phase 2 device/monitoring behavior, durable transition replay, `.bak` recovery, production path validation, single-instance locking, redacted log rotation, user lockout, reference reconciliation, bounded notification retries/dead letters, CSV/report limits, hierarchy/maps/IPAM, and isolated full/selected restore. SNMP credential protection is tested; live SNMP v2c/v3 interoperability, live Telegram/SMTP delivery, target-IIS restore, and real-device responsive browser testing are not claimed.

## Build

```powershell
dotnet build UniversalITMonitor.sln
```

## IIS deployment

For the complete Windows Server/IIS procedure, configuration example, permissions, upgrade sequence, operator checks, and recovery steps, see [DEPLOYMENT_GUIDE.md](./DEPLOYMENT_GUIDE.md), [IIS_SETUP_CHECKLIST.md](./IIS_SETUP_CHECKLIST.md), [PRODUCTION_TEST_CHECKLIST.md](./PRODUCTION_TEST_CHECKLIST.md), and [DISASTER_RECOVERY_GUIDE.md](./DISASTER_RECOVERY_GUIDE.md). These documents describe checks to perform; they do not claim target-server deployment, live-provider, responsive-device, or 200+ device load tests have passed.

1. Install the .NET 10 ASP.NET Core Hosting Bundle.
2. Publish the Web project as a framework-dependent x64 .NET 10 deployment with `dotnet publish src/UniversalITMonitor.Web/UniversalITMonitor.Web.csproj -c Release -f net10.0 -r win-x64 --self-contained false -o <publish-folder>`.
3. Create a dedicated IIS application pool using **No Managed Code**, 64-bit mode, and **Load User Profile** enabled. Use a dedicated application-pool identity.
4. Grant that identity Modify access to the published site folder when using the default data path `<publish-folder>\App_Data`; the default backup path is `<publish-folder>\Backups`.
5. No storage path setting is required for a single stable IIS site folder. Optionally set `Storage__DataDirectory` to an absolute path outside the published site when using versioned release folders or moving data independently. This lets the published files remain read-only.
6. Open the site over HTTPS and create the first administrator on the setup page. The username starts as `admin`; choose a password of at least 12 characters. Setup closes once the first account is saved.
7. Configure an HTTPS binding. Permit outbound ICMP to monitored devices and UDP to configured SNMP ports from the server. Back up the data directory before upgrades.

The process lock file is created beside the data directory. During overlapping IIS recycle, the new worker waits for the old worker to exit; configure one IIS worker process and allow sufficient shutdown time. Persistent application logs are under `<Storage:DataDirectory>\logs`; with the default path this is `<publish-folder>\App_Data\logs`. Login has no built-in MFA, so require an MFA-protected gateway or equivalent policy for administrator access. Account lockout is five failed attempts for 15 minutes in addition to the IP rate limit.

ASP.NET Core Data Protection keys are stored under the data directory and included in ZIP backups. On production Windows deployments, the default is DPAPI for the current application-pool identity. Preserve the original identity/profile and DPAPI master-key context together with the key directory to decrypt existing credentials. The app does not migrate DPAPI master keys; if that context is unavailable, restore validation blocks credentials-bearing archives rather than dropping encrypted values. For a planned cross-server deployment, configure `DataProtection__CertificateThumbprint` to a certificate in `LocalMachine\My` before creating protected credentials, grant the app-pool identity access to its private key, and install the same certificate/private key on the destination. This certificate approach protects newly generated Data Protection keys; it does not rewrap older DPAPI-protected keys. Restrict backup ZIP access: the archives are integrity checked but are not encrypted containers.
