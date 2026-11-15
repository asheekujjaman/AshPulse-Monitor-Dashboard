# AshPulse architecture and feature inventory

Inspected on 2026-11-14 before the UI redesign. This report records the implementation present in this checkout; it does not infer features from the product name.

## Architecture

| Area | Current implementation |
| --- | --- |
| Solution | `UniversalITMonitor.sln` contains five .NET 10 projects: Core contracts/models, Application services/workers, Storage, Monitoring probes, and the ASP.NET Core Web app. |
| Frontend | ASP.NET Core MVC with Razor views, local Bootstrap CSS/JS, jQuery validation, and small vanilla JavaScript modules. There is no separate SPA or frontend build pipeline. |
| Dependencies | Project references plus `Lextm.SharpSnmpLib` 12.5.7 in Monitoring. The test project uses MSTest 4 and ASP.NET Core MVC testing 10. |
| Persistence | Local JSON documents and per-device JSONL histories/samples under `App_Data` (or configured `Storage:DataDirectory`). There is no relational database. Writes are atomic per document; the application uses a single-instance lease. |
| Authentication | Local password-hashed accounts and cookie authentication. A fallback policy requires authentication. Roles are Administrator, Operator, and Read-only. Admin-only settings/administration and Admin/Operator device-management policies are enforced on controller actions. |
| ICMP | `IcmpProbe` uses `System.Net.NetworkInformation.Ping`; the hosted `MonitoringWorker` polls enabled devices with bounded parallelism and non-overlapping cycles. |
| SNMP | Optional read-only SNMP v2c and v3 collection via SharpSnmpLib. System/interface values and standard CPU/memory MIB tables are queried when supported by the agent. SNMP collection errors do not change ICMP reachability status. |
| Scheduling | `MonitoringWorker` reads JSON settings, clamps intervals to 10–3600 seconds, and waits for either the interval or an explicit cycle trigger. A separate worker handles notifications; another handles automatic backups. |
| Notifications | Status transitions are persisted to an outbox and delivered over Telegram Bot API or SMTP. Credentials use ASP.NET Core Data Protection; retry and dead-letter states are recorded. Provider delivery has not been verified with live credentials in this checkout. |
| IIS | The repository includes a published `site/web.config` for ASP.NET Core Module v2, in-process hosting, and a framework-dependent .NET 10 deployment procedure. The source Web project and IIS runbooks are the maintained inputs; `site/` is existing publish output. |

## Implemented page and feature inventory

| Feature | Existing page/route | Current access and scope |
| --- | --- | --- |
| Overview | `/Home` | Authenticated dashboard. Real device states, recent checks, locations/groups, current IPAM totals, and recent notification delivery summaries. No calculated availability KPI or active-alert entity is on this page today. |
| Device inventory | `/Devices` | Authenticated list, filters for search/status/type/location/group/monitoring, details, CSV import/export, ICMP subnet discovery, and management actions. Device types include servers; there is no separate server inventory. |
| Monitoring | `/MonitoringSettings` | Admin-only poll interval, ping timeout, and concurrency settings; an Admin-only data-integrity report is also present. ICMP/SNMP collection logic is in the Monitoring project. |
| Network topology | `/NetworkMaps` | Authenticated maps and editor; existing devices are map nodes. Admin/Operators can save, delete, and import maps. |
| Alerts and notifications | `/Notifications` | Admin-only Telegram/SMTP configuration, test-send, delivery history, and dead-letter retry. There is no separate alert-rule or active-alert page. |
| Reports | `/Reports` | Authenticated availability/status-transition/response-sample and current IPAM reports with date and inventory filters and CSV export. Availability is unavailable where recorded state history is missing. |
| IPAM | `/Ipam` | Authenticated inventory/subnet reads and CSV export; Admin-only record/subnet edits and import. |
| Locations and groups | `/Locations`, `/Groups` | Authenticated hierarchy and health summaries; Admin/Operators manage both. |
| Search | `/Search` | Authenticated global inventory search across devices, IPAM records/subnets, hierarchy, and maps. |
| Users and administration | `/UserAdministration`, `/Appearance`, `/MonitoringSettings`, `/Backups` | Admin-only user, theme, monitoring, notification, backup/restore, and integrity management. |

## Redesign constraints from the inspection

- Preserve MVC routes, form names, antiforgery behavior, authorization attributes, polling/status endpoints, and the JSON storage contracts.
- Keep the current three themes (Ocean Blue, Midnight Purple, Emerald Green). Ocean Blue is the persisted default; its current palette is not the requested AshPulse palette.
- Use existing monitoring/report data for dashboard values. Do not label recent notification history as active alerts or infer availability without a valid history baseline.
- Keep the published `site/` directory, `App_Data`, and production settings out of visual-only changes. Rebuild/publish only through the maintained Web project as part of explicit validation, not by editing deployment output by hand.
- Automated tests are in `tests/UniversalITMonitor.Tests`; target-IIS checks, real provider delivery, live SNMP interoperability, and hardware/browser responsive checks require their actual deployment environment and are not implied by repository tests.
