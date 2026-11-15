# Production validation checklist

Run this against a **staging** deployment that matches the target Windows Server, IIS, storage volumes, identities, ACLs, proxy path, and provider/network policy. The current automated suite and previous isolated tests are not production sign-off. Record results and evidence; do not mark a step complete from code inspection alone.

At package creation (2026-11-01), the repository Release build passed with 0 warnings/errors and the automated suite passed 39/39. Those results are recorded in [PROJECT_STATUS.md](./PROJECT_STATUS.md); they are not a Windows Server/IIS deployment, provider, responsive-device, or load-test result. The deployment-specific checkboxes below remain blank until run by the operator.

## Test record and result labels

- Environment/server: ______________________________
- Application release / commit: ____________________
- Tester / date (UTC): _____________________________
- Test data and provider accounts approved by: ______
- Test window / change ticket: _____________________

Use these labels in evidence:

- **Automated repo test**: run by `dotnet test`; it does not exercise IIS, real devices, or external providers.
- **Staging/manual**: performed by an operator in a non-production deployment or approved test network.
- **Production-specific**: performed on the target Windows Server/IIS instance under an approved change. A staging pass does not satisfy it.
- **Not run / blocked**: record the reason and owner; do not describe it as passed.

### Automated baseline

- [ ] **Automated repo test** — run the commands on the exact source revision being published:

```powershell
dotnet build .\UniversalITMonitor.sln -c Release --no-restore
dotnet test .\UniversalITMonitor.sln -c Release --no-build --no-restore --logger "console;verbosity=minimal"
```

- [ ] Record target framework and installed Hosting Bundle/runtime version.
- [ ] Save full command output, test totals, commit/hash, and publish artifact hash.
- [ ] Confirm automated coverage includes durable transition replay, JSON `.bak` recovery, path validation, single-instance lease, log redaction/rotation, notification retries, Admin/Read-only authorization, all themes/favicon routes, and isolated backup/restore. These test names/coverage do not imply the target server has passed.

## Install, security, and navigation smoke tests

- [ ] **Staging/manual** — verify HTTPS certificate chain, hostname, redirects, HSTS behavior, and no accidental HTTP-only login path.
- [ ] **Staging/manual** — confirm `ASPNETCORE_ENVIRONMENT=Production`, exact `AllowedHosts`, data path (`App_Data` by default or configured external path), backup path, log/key locations, and effective NTFS permissions. Confirm no writes under `wwwroot` and that the app pool can write to the publish folder when using the default.
- [ ] **Staging/manual** — sign in as Admin and Read-only test users. Admin-only user, notification, appearance, integrity, and backup/restore routes must reject Read-only requests at the server. Verify logout, antiforgery, account lockout, and that protected credential values never appear in pages, logs, browser downloads, or CSV exports.
- [ ] **Staging/manual** — exercise Dashboard, Devices, Locations, Groups, Network Maps, IPAM, Reports, Notifications, User Administration, Appearance, and Backup/Restore. Check empty states, errors, forms, filters, and keyboard focus.
- [ ] **Staging/manual** — set each of Ocean Blue, Midnight Purple, and Emerald Green, refresh, sign out/in, and confirm the default persists without changing custom branding. Confirm UP green, DOWN red, WARNING yellow, UNKNOWN gray in each theme.
- [ ] **Staging/manual** — test status details and global navigation with both roles. Confirm Read-only has no write controls and receives 403/denial on direct Admin endpoint requests.
- [ ] **Staging/manual** — inspect the browser Network panel: favicon SVG, PNG sizes, ICO, Apple touch icon, manifest, styles, and scripts must load with successful responses and no 404s. Repeat using the production IIS URL/virtual application path.

## Responsive UI verification

Use current Edge or Chrome DevTools Responsive Design Mode. Set the **CSS viewport** to each size; test both roles, all themes, and at minimum Dashboard, Devices, Locations/Groups, Network Maps, IPAM, Reports, and the Admin pages.

| Viewport | Pages and roles tested | No page-wide horizontal overflow | Tables scroll within their own container | Result/evidence |
| --- | --- | --- | --- | --- |
| 390 x 844 | ____________________ | [ ] | [ ] | ____________________ |
| 768 x 1024 | ___________________ | [ ] | [ ] | ____________________ |
| 1280 x 800 | ___________________ | [ ] | [ ] | ____________________ |

- [ ] Record browser/version, zoom 100%, screenshots, and any exceptions. Narrow tables may use an internal scroll region; the body, sidebar, header, and cards must not force page-wide horizontal scrolling.
- [ ] Check map editor controls/canvas at narrow widths and confirm touch/pointer controls remain reachable. Check login and forms at 390px.
- [ ] Recheck the status palette and text contrast in all three themes; do not recolor status badges as theme accents.

## Monitoring and notifications

- [ ] **Staging/manual** — add a controlled ICMP responder and a controlled non-responder in an approved test VLAN. Verify UP/DOWN, response time, last-check time, current state duration, status-history rows, and recovery after restoring reachability.
- [ ] **Staging/manual** — restart/recycle after a status transition is recorded. Verify current status and pending-transition recovery; verify exactly one history event per transition ID. Notification delivery is at-least-once around the provider-accepted/local-write crash window, so inspect and document possible duplicates.
- [ ] **Staging/manual** — trigger one DOWN transition and one recovery transition with a monitored test device. Verify alert contents contain device name, IP, status, location, timestamp, and safe details. Leave the device continuously down across several polls and verify there are no repeated transition alerts.
- [ ] **Telegram live integration** — use a test bot and controlled private chat/channel. Save token via Admin UI, use Test Send, confirm one message arrives, confirm failures appear without interrupting checks, and remove/revoke the test token after testing. Do not paste tokens into commands, tickets, screenshots, or logs.
- [ ] **SMTP live integration** — use a controlled relay and mailbox. Configure TLS/STARTTLS settings supported by the UI, run Test Send, confirm delivery and sender/recipient handling, test a deliberately rejected test recipient/relay credential, then correct it and retry. Verify certificate validation, relay policy, bounce handling, and safe failure text. Never disable TLS validation to make the test pass.
- [ ] Confirm one failed delivery enters bounded retry and then a visible dead-letter state; use Admin retry only after correcting the provider/configuration. Record attempt/result counts and ensure no credentials appear in history.

## SNMP and network integration

- [ ] **Lab/manual** — use an approved SNMP agent with a read-only SNMPv3 account where possible. If validating v2c, isolate it to a trusted management network and use a non-default community. Do not use command-line flags, shell history, packet captures, or ticket text that expose credentials.
- [ ] Add the lab device through the UI, set credentials, wait for configured polling, and verify only supported system/interface fields appear. Confirm unsupported CPU/memory OIDs remain unreported, and an SNMP failure does not alter ICMP status.
- [ ] Verify UDP/161 routing/firewall and agent ACLs from the app server. Record agent make/version, protocol/security level, supported OIDs, result, and safe error only.
- [ ] **Production-specific** — repeat only against approved target agents after a change approval. This repository does not claim universal/vendor-specific SNMP interoperability.

## Backup, restore, and scheduled operation

- [ ] **Staging/manual** — set the external backup directory and validate effective ACLs. Create a manual full backup. Confirm manifest validation, expected files/size, history result, and no plaintext credentials. Restrict archive downloads/storage because non-secret inventory content is readable in the ZIP.
- [ ] Copy the archive to an isolated recovery environment with a separate external data directory. Preview it, verify integrity/version/file list, and restore into that isolated directory. Verify devices, hierarchy, maps, IPAM, users, notification settings, monitoring history/samples, backup settings, branding/themes, and protected credentials with the same key context. Keep all production data untouched.
- [ ] Test a corrupt/invalid archive in isolation and verify validation refuses it without changing the existing destination. Test a full restore, selected-module restore where safe, safety-backup creation, app stop/restart, login, and post-restore monitoring.
- [ ] **Staging/manual** — enable automatic backup at the minimum approved interval (the UI permits 1 to 168 hours), retention 1 to 365, and a controlled external path. Save and record next due time. Wait for a due run; allow the worker's 30-second check cadence plus backup duration. Verify a successful result in `backup-history.json`/Admin history, correct ZIP, retention behavior, and no overlapping run.
- [ ] Restart/recycle the app before the next due run and confirm the persisted schedule still runs. Do not claim a scheduled backup passed by merely saving its settings.
- [ ] Confirm safety restore archives remain available outside automatic/manual retention deletion.

## Persistent logs and IIS lifecycle

- [ ] **Staging/manual** — trigger a safe startup and a controlled failure. Confirm JSONL entries appear under `<data>\logs`, flush after the event, rotate at configured size/date, and prune to configured file count.
- [ ] Search test log files and Admin history for test passwords, Telegram token patterns, SMTP password, SNMP community, and authorization values; verify none are present. Verify app continues operating if logging is temporarily unavailable, then restore ACL and confirm logging resumes.
- [ ] **Staging/manual** — run `appcmd list wp` before/during/after a planned recycle. Confirm there is only one configured worker process; the new process starts only after the old process releases the sidecar data lease. Confirm requests recover and monitoring/background backup workers resume. Record stop time, startup delay, lock ownership, and any ANCM/Event Log errors.
- [ ] Exercise an orderly app-pool stop, server reboot, and IIS start. Confirm no JSON write corruption, no stale process lock, Admin sign-in, monitoring, log writes, and automatic schedule after restart.
- [ ] **Production-specific** — after an approved maintenance window, verify the same behavior against the real service identity, ACLs, target storage, certificate private-key access, and recycle policy.

## Safe JSONL policy and capacity observation

Current implementation detail: per-device `monitoring-samples` and `monitoring-history` JSONL files are append-only and unbounded. There is no segment reader, archive index, or report roll-up reader. Reports expect their normal data paths. **Do not truncate, move, externally rotate, or delete active JSONL files** as a retention action. Until retention-aware application support exists, preserve the complete files, monitor volume capacity, keep verified backups, and expand/alert on storage before space is low.

Plan a retention design before deployment for the expected data lifetime. A reasonable proposal for review is: keep raw check samples online for 90 days; retain compressed, encrypted, immutable off-host sample segments for at least the approved reporting/audit period; preserve transition history for the agreed availability-report retention period (often longer than raw samples); produce and verify daily aggregates for older samples; and only prune source segments after report code reads the aggregates/archives and a restore rehearsal proves the retained metrics. This application does **not** currently implement this policy; do not execute the deletion step until the code supports it.

Sample volume formula (assuming one sample per monitored device per poll):

```text
samples per day = monitored device count * 86,400 / polling interval seconds
200 devices at a 60-second interval = 288,000 samples/day
```

Measure real JSONL byte growth on staging for the device mix and software release; do not estimate archive size from this count alone. Track available disk, per-device sample/history file sizes, backup size/duration, report latency, and log size.

## Representative 200+ device load and soak plan

Run only in a controlled staging network with synthetic inventory and owned test responders. Do not load-test arbitrary production IPs. Match the planned server hardware, Windows/IIS settings, storage media, polling interval/timeout/concurrency, notification settings, and SNMP fraction.

1. Seed 50, 200, then 400 monitored devices (or the approved target and 2x headroom). Use a mix of controlled ICMP responders, controlled unreachable addresses, and a declared subset of devices with supported SNMP agents. Avoid real customer credentials. Add enough locations/groups/maps/IPAM rows to approximate expected inventory.
2. For each size, run at least 60 minutes after warm-up; then run a 24-hour soak at the approved target size. Include a scheduled backup, a report over the widest allowed range, global search, status refresh, and one planned IIS recycle during a separately approved soak window.
3. Use Windows Performance Monitor to record `w3wp` private bytes/working set/CPU, host CPU, disk free space and write latency, app log/data/backup file growth, and HTTP request latency/errors. Record poll-cycle duration and timestamp/check spacing from persisted device fields and actual samples; compare cycles to configured interval rather than assuming the interval is achieved.
4. Exercise reports, CSV exports, maps, and filters at realistic data sizes without simultaneous destructive writes. Check lock wait/error logs and failed JSON writes. Verify each controlled device has expected latest state and actual response samples after the soak.
5. Set quantitative pass criteria before running. Recommended starting targets for review: 95th percentile poll-cycle duration no more than 75% of configured interval; no poll overlap or sustained missed intervals; no unhandled application crash; no unexplained monitoring gaps; no monotonically growing memory after warm-up; average CPU below the locally agreed sustained ceiling (for example 70%); adequate disk free reserve after measured projected growth and backups; and interactive report/request p95 under the service's approved latency objective. Tune to the site's SLO; these are proposed criteria, not measured results.
6. Retest with maximum approved concurrency/timeout and actual notification/SNMP mix. If criteria fail, reduce supported device count or polling load and plan a measured remediation before production. This checkout has no representative 200+ device benchmark result.

## Sign-off

- Build/test evidence: ______________________________
- Backup restore rehearsal archive ID / date: _______
- Telegram / SMTP / SNMP evidence references: _______
- UI viewport/browser evidence: ____________________
- IIS recycle/reboot evidence: ______________________
- Load/soak metrics and approved capacity: __________
- Open risks, owner, due date: ______________________
- Production approval: _____________________________
