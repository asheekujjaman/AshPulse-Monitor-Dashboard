# AshPulse Infrastructure Monitor — Administrator Guide

This guide covers day-to-day settings and controls available to the local Administrator role. For a first deployment on Windows Server/IIS, use the [Implementation runbook](./ASHPULSE_IMPLEMENTATION_RUNBOOK.md) and detailed [Deployment guide](../DEPLOYMENT_GUIDE.md).

## 1. Administrator responsibilities

An Administrator controls local user access, monitoring settings, notification channels, appearance, IPAM changes, and backups/restores. Administrators and Operators manage device inventory and location/group/map structures. Read-only users can view and search permitted modules and access reports but cannot make changes.

The app has no built-in MFA or Active Directory login. Use an approved MFA-protected gateway or equivalent access policy for administrators. Keep the application on trusted HTTPS and limit access to intended users.

## 2. Initial setup and accounts

On a clean data directory, first-run setup is shown. Create the initial Administrator there; the username starts as `admin`, but the password is chosen during setup (minimum length 12). Setup closes once the first account is saved. There is no universal default password.

Open **User management** to create local accounts and select Administrator, Operator, or Read-only. The edit flow allows password reset and account disablement. The application protects the last active administrator from being disabled, demoted, or deleted. Disabled/deleted accounts and changed roles are revalidated on requests.

Login applies IP rate limiting and account lockout after repeated failed attempts. If a user cannot sign in, verify the account is active and use the normal administrator password-reset workflow; never share administrator credentials.

## 3. Monitoring settings

Open **Monitoring** to configure the check interval, ping timeout, and maximum concurrent checks within accepted ranges. Keep concurrency appropriate for server resources and network policy. Polling cycles do not overlap; device or setting changes can signal an earlier cycle.

Monitoring starts with the web application and therefore depends on IIS keeping it alive. For IIS use **AlwaysRunning**, **Load User Profile**, Application Initialization/preload, one worker process, and adequate shutdown time as described in the [Deployment guide](../DEPLOYMENT_GUIDE.md).

## 4. Manage device inventory

See the [User guide's device workflow](./ASHPULSE_USER_GUIDE.md#4-add-and-maintain-devices). Administrator checks:

- Confirm the IP and type before enabling checks.
- Assign stable locations/groups where reporting and maps use them.
- Configure ICMP reachability from the application server, not the user's workstation.
- Use discovery only for approved IPv4 CIDRs; discovery is ICMP-based and limited to `/22` through `/32`.
- Review imported CSV fields and duplicate reports. Device CSV import does not set SNMP secrets.
- Check the latest result and state history after first poll. Unknown means no current result or paused monitoring.

## 5. Optional SNMP setup

Edit a device and select the supported SNMP version. Enter read-only credentials, UDP port, and v3 security settings if applicable. Credentials are Data Protection-encrypted at rest; the UI does not show saved values. A blank secret field preserves the existing secret. Device Details has **Test SNMP now** for an immediate request and an actionable response/error.

Allow outbound UDP from the web server to the approved agent/port and configure the agent's read-only view to expose system and interface MIBs. Device Details shows port state, inbound/outbound rates, and assigned VLAN when Cisco VLAN membership or standard bridge PVID tables are available. Legacy 32-bit IF-MIB counters are used when 64-bit counters are not exposed. CPU/memory depend on agent MIB support. SNMP errors remain separate from ICMP reachability. Interface hide/show choices are saved in that browser.

Live interoperability with real agents is environment-specific. Validate against a controlled target before rollout and record vendor, firmware, and security settings.

## 6. Configure Telegram and SMTP

Open **Alerts / Notifications** as Administrator. Configure the selected channel, supply valid credentials and destination, enable it, save, and use **Test send**. Confirm receipt at the actual endpoint before relying on it.

Credential fields are protected using Data Protection and are not returned to the page. The UI can indicate that a credential is saved without revealing it. Re-enter a credential only when rotating it. Restrict access to local data and backup directories.

Notifications are queued for Down and Down-to-Up transitions, not every repeated Down poll. Delivery records show channel results. Transient attempts are bounded; exhausted failures/dead letters need administrator attention. Delivery is not atomic with external provider acceptance, so a narrow crash window can produce a duplicate. Do not claim external delivery is verified until a target-environment test succeeds.

## 7. Configure IPAM and hierarchy

Register locations/buildings/areas and groups to match the organization's operational structure. Reassign devices before deleting a populated hierarchy node; move child nodes as prompted.

IPAM is an Administrator-managed address inventory. Register relevant subnets, use **Discover subnet** to scan registered IPv4 ranges from /22 through /32, and add one or several responsive hosts with their inventory details. Existing monitored devices are linked automatically. You can also import CSV records through the preview/validation step. Confirm duplicates, subnet membership, and assignment status before commit; ICMP reachability never determines ownership or assignment status.

IPAM does not configure DHCP, switches, routers, or endpoint network settings. Assignment status is explicit inventory data and is not inferred from ICMP.

## 8. Topology maps

Create maps in **Topology**, optionally scoped to a location/building. Place existing devices; nodes reference device IDs. Review connections and node placement, save, and export map JSON for a portable map file. Before importing, inspect the device-reference preview and resolve missing references. Back up the complete application separately; a map export is not a full backup.

Deleting a device removes its related saved map nodes/connections. Coordinate topology changes with inventory cleanup.

## 9. Appearance

Select Ocean Blue, Midnight Purple, or Emerald Green using the theme selector or **Appearance** page. Theme selection is application-wide; status indicators retain their semantic status colors. Preserve custom organization branding during upgrades and validate it across themes.

## 10. Backups and restore

Open **Backup and restore** to configure automatic interval/retention and an absolute destination, create a manual ZIP, download it, and inspect the recorded result. The default backup directory is `Backups` beside the data directory's parent/application directory. The app creates it when a backup operation requires it.

Before restore:

1. Identify the correct archive and destination instance.
2. Use restore preview and review the archive version, manifest, files, and modules.
3. Confirm compatibility of credential keys and Data Protection context.
4. Confirm the safety backup can be written and sufficient disk space is available.
5. Start full or selected-module restore only after approving the replacement target.
6. After full restore, let the application restart and verify login, inventory, monitoring, alerts, and backup configuration.

Backups are integrity-checked ZIP files, not encrypted containers. Sensitive provider credentials remain Data Protection ciphertext, but inventory/settings/report data can be readable. Restrict local ACLs and store off-server copies on an encrypted, access-controlled destination.

Do not copy, delete, extract over, or manually edit live JSON/JSONL files while the app is running. Use the application restore workflow or a controlled offline procedure.

## 11. Storage locations and permissions

By default, data is stored in `<application content root>\App_Data`, outside `wwwroot`. At local development runtime this is normally the Web project folder; in a stable IIS deployment it is the publish folder. Logs and Data Protection keys are beneath the data directory. Per-device monitoring history/samples and SNMP metrics use child directories as needed. Backups default to a sibling `Backups` folder.

The application creates its default data/log/key directories and creates backup destinations when used. With the in-site default data location, the IIS application-pool identity needs Modify access to the publish folder because the process lock and restore staging are siblings of `App_Data`. For versioned release folders, configure `Storage__DataDirectory` to a stable absolute external path and grant Modify to configured data/backup locations. Preserve data and keys across upgrades.

Keep `App_Data` outside `wwwroot`. Do not grant broad write access such as `Everyone` or `Users`. Use the [IIS setup checklist](../IIS_SETUP_CHECKLIST.md) for target-specific path and ACL records.

## 12. Routine checks and incident triage

| Symptom | Operator action |
| --- | --- |
| Device becomes Down | Check latest error, server-to-device route/firewall/ICMP, and device state. Verify recovery after connectivity returns. |
| SNMP data disappears | Validate credentials, version/security level, UDP path, and agent-supported OIDs. Do not equate SNMP failure with device Down. |
| Alert delivery fails | Inspect per-channel history, correct provider settings/network, test-send, then retry dead-lettered channels as appropriate. |
| App reports storage failure | Inspect `<data directory>\logs`, disk space, identity ACLs, and configured data path. Preserve data and avoid manual repair. |
| Report has unavailable history | Confirm check/transition history exists for the selected period. Do not infer missing monitoring data. |
| Data integrity page has findings | Review the reconciliation entry, correct source inventory references, and record the resolution. Avoid deleting JSON files to hide a warning. |
| Backup or restore fails | Preserve the archive and failure record; inspect compatibility, free space, ACLs, and key context. Do not extract over live data. |

## 13. Operational boundaries

- Storage is local JSON/JSONL, designed for one application instance, not a multi-node/shared-folder deployment.
- Monitoring samples are append-only and no automatic compaction/retention policy is implemented. Monitor disk usage; do not truncate active files externally.
- The app has no Active Directory integration or built-in MFA.
- Live Telegram, SMTP, and SNMP behavior must be validated with actual providers/devices.
- Reports use persisted data; missing history stays unavailable/unverified. PDF is browser print-to-PDF.
- The source does not configure trusted reverse-proxy forwarded headers. Deploy directly behind IIS TLS unless proxy support is added and validated.

## 14. Administrator handover checklist

- [ ] Identify site owner, application owner, security contact, alert recipient, and recovery owner.
- [ ] Confirm HTTPS, app-pool identity, data path, and effective ACLs.
- [ ] Create named accounts and verify roles using a Read-only test account.
- [ ] Confirm a controlled device checks successfully from the server.
- [ ] Configure and test notification channels before operational reliance.
- [ ] Create a full backup and verify restore in an isolated data directory.
- [ ] Record external storage/encryption, retention, and restore approval process.
- [ ] Complete the [production test checklist](../PRODUCTION_TEST_CHECKLIST.md) and retain evidence.
