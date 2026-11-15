# Project status

## Current phase: Windows Server and IIS deployment validation package

Production hardening preserves the completed Phase 1-5 device, monitoring, inventory, notifications, reports, user-administration, backup, and restore features while addressing the audit findings below.

### Implemented

- Preserved Phase 1 local administrator setup, password hashing, cookie authentication, role authorization, antiforgery enforcement, and protected JSON document storage.
- Preserved Phase 2 device CRUD, CSV import/export, ICMP monitoring, status history, SNMP configuration, and monitoring settings.
- Preserved Phase 3 stable-ID hierarchical locations/groups, migration of legacy text assignments, device assignment, status summaries, interactive persistent maps linked to device IDs, JSON map import/export and missing-reference checks, and JSON-backed IPAM with CIDR registration and duplicate-safe CSV workflows.
- Added Telegram and SMTP email notification settings, credential-presence indicators, channel enable/disable, and administrator test-send controls. Telegram tokens and SMTP usernames/passwords are Data Protection-protected in local JSON storage.
- Queued one notification for a transition to DOWN and one for recovery from DOWN to UP. Unchanged continuous DOWN checks do not repeat alerts. A separate worker records delivery results and safe failure details without interrupting monitoring.
- Added Administrator-only local user create/edit/disable/delete and password reset flows, preserving the existing Operator role for compatibility. Passwords are hashed. Atomic repository checks prevent disabling, demoting, or deleting the last active administrator. Current cookie roles and enabled state are revalidated against JSON on each request.
- Added authenticated UTC-date reports for transition-based availability and UP/DOWN/UNKNOWN duration, persisted status transitions, measured response-time samples, current location/group status summaries, and current IPAM inventory. Reports filter by date, device, location, nested group, current device status, and IP assignment status.
- Added CSV exports with consistent availability report columns and spreadsheet-formula prefix handling. Report pages support browser printing and Save as PDF.
- Added append-only JSONL check samples to support real response-time statistics. Reports identify unavailable or unverified history; no historic response data is inferred before sample collection began.
- Integrated Reports into the dashboard/navigation and exposed Notifications and User Administration links to Administrators only.
- Updated [README.md](./README.md), [CHANGELOG.md](./CHANGELOG.md), and [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md) for Phase 4 and documented unverified integrations and limitations.
- Added Admin-only manual full ZIP backup, automatic schedule/retention settings, configured backup directory validation, and bounded operation history.
- Added versioned backup manifests with complete file lists, sizes, and SHA-256 hashes. Archive validation rejects path traversal, links, duplicate/missing/extra entries, size overflows, unsupported future major versions, invalid JSON and invalid cross-file references.
- Added restore preview, explicit replacement confirmation, full and selected-module restore, a verified safety backup before replacement, staged validation, same-volume directory swap with rollback, and restart scheduling after full restore. Failed validation leaves live data untouched.
- Added a process-local maintenance gate around monitoring, notifications, and ordinary web requests during snapshots/restores. Automatic/manual backups are serialized and retention does not remove safety archives.
- Included JSON/JSONL data and the Data Protection key ring in archives. Credential-bearing archives are rejected when the available Data Protection context cannot open encrypted values; credentials are never decrypted into the archive.
- Added optional certificate-based Data Protection key wrapping for planned migrations, with documented IIS permissions, backup directory placement, DPAPI identity requirements, and cross-server limits. Backups are integrity checked but are not encrypted ZIP containers.
- Added separate-temporary-data-directory tests for completeness/integrity, invalid archive rejection, full restore, selected-module restore safety, credential-key compatibility, automatic retention, and safety backup creation.
- Updated [README.md](./README.md), [CHANGELOG.md](./CHANGELOG.md), and [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md) for Phase 5 and documented migration constraints.

### Verification

- `dotnet build UniversalITMonitor.sln --no-restore` - passed with 0 warnings and 0 errors after Phase 5 changes.
- `dotnet test UniversalITMonitor.sln --no-build --no-restore` - 28 passed, 0 failed, 0 skipped after Phase 5 changes.
- `problems` - no errors found.
- Automated tests cover Phase 2 device/monitoring and authorization regressions; Admin/Read-only route access; password hashing and last-active-administrator protection; notification DOWN/recovery transition deduplication, safe secret storage, and persisted delivery failure; availability duration math, real response samples, CSV layout/content, and missing historical data. Phase 3 migration, map/IPAM validation and import/export tests remain in the suite.
- Phase 5 restore tests created a backup in one temporary JSON directory, restored it into a separate temporary directory, and checked devices, maps, IPAM, users, monitoring history/samples, SNMP snapshots, settings, and Data Protection credential decryption. Selected-module restore and invalid-key rejection were checked for preservation of live files. Admin/Read-only backup-route authorization and antiforgery rejection are automated tests.
- No Phase 5 interactive browser smoke test was run in this pass. The shared browser tab was an existing Read-only Access Denied page; the automated web tests exercised the Admin and Read-only backup routes.
- Browser smoke test used an isolated temporary JSON data directory. Admin opened the dashboard, user administration, and notification settings and created a temporary Read-only account. Read-only login showed only shared navigation, opened Reports, and was denied at `/Notifications` and `/UserAdministration`. The temporary web server was stopped after the test.
- Live Telegram delivery, live SMTP delivery, and live SNMP interoperability have not been tested against provider credentials, SMTP infrastructure, or a responding SNMP agent.

### Known limits and unverified behavior

- Telegram Bot API and SMTP sender implementations, credential protection, test-send UI, transition queuing, and failure recording are implemented. External delivery success, rate limits, and SMTP relay behavior must be verified in the deployment environment. Do not rely on alerts until a configured test send succeeds.
- Notification delivery is at-least-once around a narrow process-crash window after an external provider accepts a message but before its delivery result is saved.
- A provider failure is stored as a completed attempt and is not automatically retried; admins need to correct settings and verify them with test-send before relying on future alerts.
- Active Directory settings/authentication are not present in this checkout. Local accounts are the supported authentication path; no AD integration is claimed.
- Report availability is calculated from persisted state transitions; UNKNOWN duration is excluded from the availability percentage, and missing baselines are marked unverified or unavailable. Per-check response samples start with Phase 4, so older response-time history is unavailable. Location/group and IPAM reports are current snapshots, not historical snapshots.
- Monitoring sample JSONL files are append-only and have no retention/compaction policy. Report ranges are limited to 366 days but scan the per-device history/sample files, which grow over time.
- PDF output is browser print-to-PDF; the application does not generate server-side PDF files.
- Excel IPAM import/export is not included. IP conflict status is explicit inventory data, not an active network scan. IPAM does not configure DHCP, network devices, or operating system IP settings.
- Live SNMP v2c/v3 interoperability has not been tested. CPU/memory metrics depend on standard MIB implementations and unsupported values remain empty.
- JSON operations assume one application instance and local files. Writes are atomic per document, but multi-document device/map/IPAM workflows and external notification delivery are not transactional.
- Windows DPAPI-protected key rings remain tied to their source identity/profile and DPAPI master-key context. Copying the data directory to another server is insufficient to decrypt existing protected credentials, and this app does not migrate DPAPI master keys. Certificate wrapping must be configured before credentials are created; it protects newly generated keys and does not rewrap existing keys. Restore blocks if the key context cannot decrypt credential ciphertext.
- Backup ZIPs are not encrypted containers. Credentials remain Data Protection ciphertext, but inventory, reports, file names, and other JSON are readable by anyone with archive access. Restrict ACLs and use encrypted off-server storage.
- Full restore and selected-module restore passed automated tests using temporary data directories. A production Windows Server migration, IIS ACL/DPAPI profile transfer, and recovery from a real deployed backup have not been exercised.

### Phase 4 test coverage

- Local-user password creation/reset hashing, last-active-administrator protections, and role-based access.
- Notification transitions, duplicate suppression, message fields, credential protection, and persisted delivery failures.
- Report state-duration math, current hierarchy/IPAM summaries, response-time samples, CSV column layout, and unavailable historical data.
- Admin/Read-only web authorization for settings and management pages versus authenticated report access.

### Phase 5 test coverage

- Backup file completeness, manifest hashes, archive corruption, traversal/missing manifest handling, and secret-ciphertext preservation.
- Full restore into an independent temporary data directory, restored device/map/IPAM/user/history/sample/SNMP/settings verification, and destination-specific backup configuration.
- Selected-module restore dependency checks, live-data preservation after validation failure, Data Protection key mismatch rejection, and safety-backup-before-replacement behavior.
- Automatic schedule/retention behavior, safety archive retention, Admin/Read-only route checks, and antiforgery enforcement.

### Next

For planned cross-server migrations, configure certificate-based Data Protection key wrapping before creating protected credentials and retain the certificate private key securely. Existing DPAPI-protected credentials require the original Windows DPAPI context. Configure and test backups in the target environment, then verify provider delivery and monitor local sample-file growth.

## Production readiness audit — 2026-10-31

### Scope and verification

- Reviewed the solution structure, application source, configuration, [README.md](./README.md), [CHANGELOG.md](./CHANGELOG.md), and [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md). No application feature or production data was changed; this section is the only audit change.
- `dotnet build UniversalITMonitor.sln --no-restore` passed with 0 warnings and 0 errors. `dotnet test UniversalITMonitor.sln --no-build --no-restore --logger "console;verbosity=normal"` passed: 28 passed, 0 failed, 0 skipped. The configured NuGet vulnerability audit reported no vulnerable packages from its current sources.
- Browser smoke testing used a separate temporary JSON data directory and a separate local app instance. Admin completed first-run setup, created and previewed a ZIP backup, and completed a confirmed selected-module restore; the restore created a safety backup and restored the changed setting. An invalid archive produced a safe error. Read-only could open Devices, Locations, Groups, Network Maps, IPAM, and Reports, and was denied access to Backups, Notifications, and User Administration. Admin-only navigation entries were absent for Read-only. No production data was accessed or modified.
- Automated tests exercised a full restore into a separate temporary data directory. The browser restore smoke test covered selected-module restore, not full restore. Neither IIS/Windows Server recovery nor live SNMP, Telegram, or SMTP interoperability was tested. No penetration test, real mobile-device test, or load benchmark was performed.

### Critical security issues

No critical security defect was identified in the reviewed code and checks. This is a source and smoke-test audit, not a penetration test or proof that a deployment is secure.

### Findings by priority

#### High

- **Monitoring transitions are not durably coupled to history and notifications.** [MonitoringWorker.cs](./src/UniversalITMonitor.Application/Monitoring/MonitoringWorker.cs) first commits current device status through `RecordChecksAsync`, then separately appends samples, status history, and notification work. If a process exit or storage error occurs after status commit, the next poll sees no transition and does not recreate the missing history or alert. Persist an idempotent transition/outbox entry with the status update, then drain it with retry and reconciliation; add crash-point tests.
- **Default data and backup paths can be under the published application directory.** [Program.cs](./src/UniversalITMonitor.Web/Program.cs) defaults data to `<ContentRoot>/App_Data` and only rejects a path under `wwwroot`; [BackupService.cs](./src/UniversalITMonitor.Application/Backups/BackupService.cs) defaults backups to the data directory's parent plus `Backups`. With defaults, an IIS identity may need write access to the published site and deployment cleanup can remove data or backups. Require an explicit external data path in Production (or use a protected ProgramData path), reject paths within the entire content root, and keep backup storage external with narrowly scoped ACLs.
- **IIS logging is console-only.** [Program.cs](./src/UniversalITMonitor.Web/Program.cs) clears logging providers and adds Console. No persistent, rotated application log sink is configured. Configure an ACL-protected, rotated Windows Event Log or file sink with retention and secret redaction; verify startup and runtime failures remain available after an app-pool restart.
- **Maintenance and backup/restore coordination is process-local.** The documented single-instance assumption is not enforced across app processes. IIS overlapped recycle or a web garden can allow two workers to poll, snapshot, or restore the same directory; per-file JSON locks do not make the multi-file maintenance gate global. Enforce a process-wide/exclusive maintenance lock and a single active worker, and validate IIS recycle and shutdown behavior before deployment.

#### Medium

- **Missing JSON primary files do not consult their recovery copy.** [FileJsonDocumentStore.cs](./src/UniversalITMonitor.Storage/Json/FileJsonDocumentStore.cs) uses `.bak` after a JSON parse error, but if the primary file is absent, reads return an empty/default document. Check for and validate `.bak` when the primary is missing, restore it atomically, and report recovery rather than silently presenting empty data.
- **Cross-document mutations can leave stale references after interruption.** Device and hierarchy workflows update device, IPAM, map, and hierarchy JSON documents in sequence, not as one transaction. Location/group deletion updates linked devices but does not reconcile unlinked IPAM records or map scope IDs. Add a durable operation journal or reconciliation pass that detects and repairs/reports dangling IDs; cover interruption between each document write.
- **Transient notification failures are not retried.** A failed Telegram/SMTP delivery is recorded as a completed attempt; continuous DOWN deduplication works, but a transient provider outage can permanently lose that alert. Add bounded exponential retry with attempt classification, dead-letter visibility, and admin retry; retain idempotent transition IDs. Exactly-once delivery cannot be guaranteed across an external provider and local JSON without provider support.
- **Import endpoints can materialize large request data before row limits apply.** Device CSV has a 2.1 MB request limit, while IPAM/map request paths do not consistently cap the HTTP body before deserialization; IPAM's 10,000-row check follows CSV parsing. Apply explicit body, field, row, column, node, and connection limits before materialization, and stream CSV where practical. Bound export size or stream exports instead of building full CSV strings in memory.
- **Backup archives are readable and their hashes are not an authenticity check.** Secrets remain Data Protection ciphertext, but ZIP contents include readable inventory and other JSON. An unkeyed SHA-256 manifest detects accidental corruption, not an attacker who can replace both data and manifest. Restrict archive ACLs, encrypt off-host copies, and use a signed manifest or authenticated storage when archive tampering is in scope.
- **Authentication hardening is limited.** Login has an IP-based rate limit, but no account lockout or MFA. Before exposing the app beyond a trusted network, add account-aware throttling/lockout with safe recovery and MFA for administrators; verify forwarded client IPs are accepted only from configured trusted proxies.
- **Mobile layout overflows horizontally.** At a 390 px browser viewport, Dashboard, IPAM, Network Maps, and Reports had document width about 846 px; widths 768 and 1280 px did not overflow. Fix the small-screen navigation/content layout and repeat viewport checks. This was a browser viewport test, not a physical-device test.

#### Low / incomplete scope

- Excel IPAM import/export, Active Directory authentication, and server-side PDF generation are not implemented. Current report printing uses browser print-to-PDF. These are documented product limits; no integration is claimed.
- The source scan identified 162 LINQ call sites, 26 `new List` and 5 `new Dictionary` construction sites, and one `Substring` site across the scanned C# source. A heuristic `.Result`/`.Wait` search produced 24 text matches, all reviewed as DTO `Result` properties rather than blocking task waits. No `async void`, raw literal `Contains`/`StartsWith`/`EndsWith` calls without comparison options, or explicit per-call `new HttpClient` sites were found. These counts are triage signals, not performance measurements; no load benchmark was run.

### Performance and safe retention recommendation

- Append-only per-device JSONL history and sample files have no retention or compaction. Report windows are limited to 366 days, but each report scans the full per-device file and materializes the selected window. Keep transition history separately for availability calculations; retain raw check samples online for a configurable period (for example, 90 days), then archive verified immutable segments and compact older samples into daily counts and response-time aggregates. Preserve enough aggregate detail for the promised report fields, and never delete source segments until archive integrity and restore have been verified.
- Rotate JSONL by time or size, index segment ranges, bound device count and concurrent checks, and stream exports. Benchmark representative device counts and history sizes before choosing production polling intervals, retention, and worker limits. Whole-document JSON rewrites and unpaged list/export paths also grow in cost with inventory size.
- SNMP collection is bounded by configured concurrency but performs several sequential requests per device with per-request timeouts. Large SNMP inventories may make a poll cycle exceed its interval. Measure cycle duration/lag and set an operational device/concurrency budget; do not assume the configured interval is the achieved cadence.

### Deployment blockers and required pre-deployment checks

- For a fixed IIS site folder, the app-pool identity needs Modify on the publish folder to create the default in-site data and restore staging. For versioned releases, set stable external data and backup paths and provision Modify rights there. Confirm staging/restore swaps work on the selected volume.
- Configure HTTPS binding and certificate permissions. If deployed behind a TLS-terminating proxy, configure trusted forwarded headers so redirect scheme, secure cookies, and IP-based rate limits use the original request values. Follow Microsoft's [IIS logging diagnostics](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/logging-and-diagnostics?view=aspnetcore-10.0), [ASP.NET Core logging](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-10.0), and [proxy/load balancer guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).
- Configure Data Protection key persistence and migration wrapping before creating credentials. Prove a backup can be decrypted/restored under the intended Windows identity and key material. Do not copy DPAPI-bound data without its original compatible DPAPI context or silently discard protected values.
- Exercise publish and restore on the target Windows Server/IIS version, including app-pool identity, permissions, overlapped recycle behavior, scheduled-backup restart, log retention, and recovery from a copy of the intended production backup. Configure and test real SMTP/Telegram/SNMP integrations before relying on them.

### Audit conclusion

The solution builds and its current automated suite passes, and isolated browser checks verified core Admin/Read-only boundaries and selected restore flow. Production deployment is **not yet readiness-verified** because of the data-path default, missing durable transition/outbox handling, lack of persistent IIS logging, process-local maintenance coordination, and untested target-server recovery. No application fixes were made as part of this audit; resolve and review the findings before production release.

## Production hardening and UI enhancement follow-up - 2026-11-01

This follow-up records changes made after the 2026-10-31 audit above. The audit section remains as a historical snapshot; the fixes below supersede its open findings where specified.

### Audit findings addressed

- Monitoring status and pending transition events are committed together in `devices.json`. The worker replays each event idempotently into status history and the notification outbox before acknowledging it. Crash replay regression tests cover retry after an interrupted drain. Provider delivery remains at-least-once across the narrow accept-before-result-write window.
- The default data directory is `App_Data` under the published application and is created at startup; an absolute external `Storage:DataDirectory` remains available for versioned releases. The default backup directory is `Backups` beside the data directory. Data and backup paths remain separate and outside `wwwroot`; path checks reject linked/reparse ancestors.
- Added persistent JSONL application logging with secret redaction, bounded messages, per-event flush, size rotation, and retention. Logging IO errors are contained so they do not interrupt monitoring. Redaction and rotation are tested; IIS ACL and restart behavior are not.
- An exclusive sidecar file lease serializes processes using the same data directory, including overlapping IIS workers. A temporary-directory test verifies the second process waits for release. Actual IIS overlapped recycle/shutdown behavior remains a deployment check.
- A missing primary JSON document now recovers from a valid `.bak`; invalid backups remain preserved and fail initialization. Cross-document reconciliation repairs unambiguous stable links and records unresolved references in an Admin-only report without deleting retained map data.
- Telegram/SMTP retries are bounded, failures reach a visible dead-letter state, and Admin retry is available. Import and export request sizes are capped for device/IPAM CSV, map JSON, and report output. The production test-send path against actual providers remains unverified.
- Persisted account lockout now blocks sign-in after five failed attempts for 15 minutes alongside the existing IP rate limit. No built-in MFA was added; Admin access still requires an MFA-protected deployment gateway or equivalent control.
- Responsive CSS now changes navigation/content behavior at narrow widths and limits horizontal overflow. Browser viewport geometry could not be rechecked because the shared browser client was unavailable in this session.

### UI, themes, and favicon

- Added persistent Ocean Blue, Midnight Purple, and Emerald Green palettes while retaining custom branding and fixed UP/DOWN/WARNING/UNKNOWN status colors. Automated web tests render the branded Login screen, Dashboard, Devices, Locations, Groups, Network Maps, IPAM, Reports, and Admin routes under each theme.
- Added the low-opacity network pattern and monitoring shield favicon SVG, PNG, ICO, and Apple touch assets. The shared identity appears on the login screen. The web manifest uses relative icon paths and a relative scope for IIS virtual applications.
- Automated HTTP tests verify all icon/manifest routes and MIME types and check fixed status color CSS. A prior isolated HTTP smoke check also returned the favicon assets successfully. No visual browser test at 390px, 768px, or 1280px was completed in this session.

### Verification for this follow-up

- `dotnet build UniversalITMonitor.sln --no-restore` - passed, 0 warnings and 0 errors.
- `dotnet test UniversalITMonitor.sln --no-build --no-restore --logger "console;verbosity=minimal"` - passed, 39 passed, 0 failed, 0 skipped.
- The regression suite covers transition outbox replay, `.bak` recovery, external production paths, exclusive process lease, logger redaction/rotation, theme persistence/branding, Admin and Read-only route boundaries, provider retry/dead-letter behavior, input limits, reference reconciliation, and isolated backup/restore behavior. It renders the main Admin/Read-only app route set with test identities; this is not a real-user browser login session.
- The shared integrated browser could not be controlled (`No client available to run readPage`), so no new interactive browser, responsive geometry, Admin/Read-only sign-in, or real-world IIS smoke test is claimed. No production data was changed.

### Remaining risks and deployment work

- Production Windows Server/IIS permissions, overlapped recycle, shutdown timing, log persistence, and a restore under the target identity must be verified on the actual deployment. The lock file can make a new process wait for the prior owner to exit.
- Local JSONL monitoring history and samples still grow without retention/compaction. Reports scan persisted history; establish and test a retention/archival policy before long-running high-volume use.
- Multi-document device, hierarchy, IPAM, and map changes remain sequential rather than transactional. Startup reconciliation reports and repairs supported dangling references, but it is not an operation journal and cannot make every multi-file update atomic.
- Real Telegram/SMTP delivery, SNMP interoperability, production backup authenticity/encryption, built-in MFA, AD authentication, Excel import/export, and server-side PDF generation remain unverified or out of scope. ZIP backup hashes detect corruption but do not authenticate an archive against a malicious replacement.
- Responsive styles and theme tokens have automated coverage, but visual contrast and layout across real browsers/devices still need manual viewport checks. No load/performance benchmark or penetration test was run.

### Updated production readiness position

The high and medium findings specifically addressed above have corresponding code changes and automated coverage. The application is not declared production-ready: deployment-specific IIS/Windows/Data Protection recovery, external provider integrations, archive confidentiality/authenticity controls, responsive visual review, retention policy, and representative load/security testing remain open.

## Windows Server and IIS deployment package - 2026-11-01

Reviewed the latest production-hardening status, README, architecture guidance, appsettings, IIS/backup configuration, Data Protection setup, and automatic backup worker. This update adds operator documentation only; it does not change application behavior or claim a deployment was performed.

### Package created

- [DEPLOYMENT_GUIDE.md](./DEPLOYMENT_GUIDE.md) documents the .NET 10 framework-dependent publish, production appsettings example, actual data/key/log/backup path behavior, IIS/TLS setup, upgrade/rollback, and runbook references. It records the Microsoft .NET 10 download page's ASP.NET Core Runtime 10.0.14 listing observed on 2026-11-01 and requires the current supported servicing release at deployment time.
- [IIS_SETUP_CHECKLIST.md](./IIS_SETUP_CHECKLIST.md) provides target-specific checkboxes for Hosting Bundle, NTFS permissions, application-pool identity/settings, startup, HTTPS, backups, and lifecycle sign-off.
- [PRODUCTION_TEST_CHECKLIST.md](./PRODUCTION_TEST_CHECKLIST.md) separates automated repository tests from staging/manual and production-specific checks; includes UI, integration, scheduled backup, IIS recycle, responsive viewport, JSONL retention, and 200+/400-device load/soak procedures.
- [DISASTER_RECOVERY_GUIDE.md](./DISASTER_RECOVERY_GUIDE.md) documents archive validation/restore, isolation, failure recovery, Data Protection DPAPI/certificate handling, and cross-server migration.
- Updated [README.md](./README.md), [CHANGELOG.md](./CHANGELOG.md), and [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md) to link the runbooks and state that they are procedures, not completed deployment tests.

### Configuration facts recorded

- The app targets `net10.0`; framework-dependent IIS hosting requires the x64 .NET 10 ASP.NET Core Hosting Bundle (runtime plus ANCM). The IIS server does not require the SDK for this deployment pattern.
- The default `Storage:DataDirectory` is `App_Data` under the published app; the application creates the directory, JSON files, Data Protection keys, and logs. An absolute external override is available for versioned releases.
- Backup directory is persisted in `backup-settings.json`, not an appsettings setting. It defaults to `Backups` beside the data directory, separate from data and `wwwroot`; configure a separate absolute location through the Admin UI when needed.
- A reverse-proxy deployment is flagged as unsupported without trusted forwarded-header middleware/configuration. Arbitrary forwarded headers and the unrestricted forwarded-header environment switch are prohibited in the procedure.
- Monitoring history/sample JSONL retention remains unimplemented. The checklist says not to externally rotate, move, truncate, or delete active files and proposes a future 90-day raw-sample/verified-archive policy only after report-aware segments/aggregates are implemented.

### Verification performed for this update

- `dotnet build UniversalITMonitor.sln -c Release --no-restore` - passed, 0 warnings and 0 errors.
- `dotnet test UniversalITMonitor.sln -c Release --no-build --no-restore --logger "console;verbosity=minimal"` - passed, 39 passed, 0 failed, 0 skipped.
- These are repository automated tests against the current source. No Windows Server/IIS installation, publish cutover, ACL, HTTPS binding, scheduled job, IIS recycle, production restore, responsive-device, live Telegram/SMTP/SNMP, or 200+ device load test was performed as part of this documentation update. The new checklist checkboxes are intentionally blank.

### Deployment status

The deployment package is prepared; target deployment validation remains pending. Before production use, complete the IIS checklist and applicable production test steps, verify a restore under the target identity and key context, perform approved provider/network tests, and approve storage capacity/retention and load criteria. This documentation update does not change the existing JSON-only, single-instance architecture.
