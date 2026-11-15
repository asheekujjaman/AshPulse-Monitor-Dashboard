# AshPulse Infrastructure Monitor — Training Guide

This guide provides an instructor-led session and hands-on lab for the implemented AshPulse UI. Adjust timing to the audience and available permissions.

## 1. Training outcomes

By the end of training, participants should be able to:

1. Sign in and explain Up, Down, and Unknown accurately.
2. Find a device and read current status, last check, response, and history.
3. Perform inventory actions allowed by their role.
4. Use locations/groups, topology, and IPAM without confusing them with device monitoring.
5. Read reports while recognizing unavailable history and current-snapshot sections.
6. Describe when to escalate a failed check, SNMP issue, or failed notification.
7. For Administrators, manage users/settings, test alerts, create a backup, and locate recovery guidance.

## 2. Audience and prerequisites

### Participant tracks

- **Read-only users:** observe inventory, search, view maps/IPAM/reports, and export where allowed.
- **Operators:** complete the Read-only labs plus permitted device and location/group/map maintenance.
- **Administrators:** complete all labs plus user, monitoring, notification, theme, backup, and restore-preview work.

### Trainer preparation

- Use a non-production training instance or approved isolated test environment.
- Confirm the instance is running and uses HTTPS if reachable over a network.
- Prepare individual accounts for each role. Do not give trainees shared Administrator credentials.
- Create a few approved lab devices or clearly identify why a lab device has no response. Never scan an unapproved range or use a live target without authorization.
- Prepare a sample CSV from the application's downloaded template and one map if realistic records are useful.
- If teaching notification setup, use an approved test destination. Do not place real provider secrets in slides or handouts.
- Have an isolated path for backup/restore rehearsal. Do not restore training data over production.

## 3. Suggested two-hour session

| Time | Module | Audience |
| --- | --- | --- |
| 0–10 min | Goals, roles, access, safe use of lab systems | Everyone |
| 10–25 min | Sign in, dashboard, status meanings, global search | Everyone |
| 25–45 min | Device details and role-permitted device operations | Everyone / role-specific |
| 45–60 min | Locations, groups, topology concepts | Everyone |
| 60–70 min | Break | Everyone |
| 70–85 min | IPAM and subnet inventory | Everyone; edits Admin-only |
| 85–100 min | Reports, filters, CSV, print-to-PDF | Everyone |
| 100–115 min | Monitoring, alerts, users, backup | Administrators |
| 115–120 min | Knowledge check, escalation contacts, feedback | Everyone |

For a shorter Operator/Read-only session, omit the Administrator track and deliver the [Administrator guide](./ASHPULSE_ADMINISTRATOR_GUIDE.md) separately.

## 4. Instructor walkthrough

### A. Sign in and orient

1. Show the sign-in page and application navigation.
2. Explain that the first administrator is chosen at installation; there is no shared preset password.
3. Point out role-dependent navigation and explain that hidden links do not grant permissions.
4. Open Overview and identify current counts, current status, and unavailable-history states.
5. Explain: Up means latest ICMP response; Down means no latest ICMP response; Unknown means no current check or monitoring paused.

### B. Find a device and read details

1. Search for a device by name or IP address.
2. Open device details.
3. Review current status, response time, last check, state duration, and status history.
4. Demonstrate the IP copy icon and verify the copied value in a safe text field.
5. Explain that SNMP adds telemetry; SNMP failure alone does not change ICMP status.

### C. Device and CSV exercise

Administrators/Operators can perform the lab change; Read-only trainees should describe the workflow without submitting.

1. Add or edit one approved lab device with a clear name and correct IP.
2. Assign it to a training location/group.
3. Confirm that it shows Unknown until a check occurs; then review the observed result.
4. Download the device CSV template and identify the required name and IP columns.
5. Show import preview and duplicate handling. Cancel or commit only lab data.

### D. Hierarchy and topology exercise

1. Open the training location and group.
2. Show how assignments determine which devices appear under a hierarchy node.
3. Open or create a lab topology map.
4. Place an existing inventory device, position it, and connect it to another lab node if available.
5. Select the device node to open its inspector and copy its IP.
6. Export the map as JSON and explain that this is not a full application backup.

### E. IPAM and reports exercise

1. Open an IPAM record and distinguish explicit assignment status from ICMP reachability.
2. Review subnet validation and duplicate reporting in an import preview.
3. Open Reports and choose a date range in UTC.
4. Point out unavailable/unverified historical data and current-snapshot location/IPAM sections.
5. Export CSV. Use the browser print dialog to demonstrate Save as PDF.

### F. Administrator lab

1. Create a named Read-only training account. Verify that it can open a permitted page and cannot open an Admin-only page.
2. Review polling interval, timeout, and concurrency. Do not change production settings during class.
3. Configure a training notification channel or inspect the existing one, then Test send only to an approved destination.
4. Create a manual backup in the lab. Review its history/preview and confirm the file location.
5. Perform restore preview only, or restore into a separate temporary training data directory with an instructor observing.
6. Explain that backup ZIPs are not encrypted containers, the application has no built-in MFA, and Data Protection keys have environment-specific handling.

## 5. Participant hands-on checklist

### All users

- [ ] Sign in with an individual account and identify your role.
- [ ] Find a device by name and by IP.
- [ ] Explain current status without treating Unknown as Down.
- [ ] Open device details and identify its last check and history.
- [ ] Copy an IP address with the copy icon.
- [ ] Open a location or group and find its device list.
- [ ] Open a topology map and locate a device inspector.
- [ ] Open an IPAM record and distinguish assignment status from monitoring status.
- [ ] Apply a report filter and explain an unavailable value.

### Administrators and Operators

- [ ] Add or edit only an approved lab device.
- [ ] Pause/resume monitoring and explain the resulting state behavior.
- [ ] Review a CSV import preview without committing unintended records.
- [ ] Assign a device to a lab location/group and find it in hierarchy views.
- [ ] Save or export a lab topology map.

### Administrators only

- [ ] Create a least-privilege training account and verify its permissions.
- [ ] Locate monitoring settings and explain each setting before changing it.
- [ ] Test an alert channel at an approved endpoint and verify receipt.
- [ ] Create a manual backup and identify where it is stored.
- [ ] Open restore preview and describe the pre-restore safety-backup step.
- [ ] Identify the person responsible for production restore approval.

## 6. Knowledge check

1. A device shows Unknown. Does that prove it is unreachable? **No.** It may not have been checked yet or monitoring may be paused.
2. A device responds to ICMP but SNMP has an error. Is the device automatically Down? **No.** ICMP reachability and SNMP telemetry are separate.
3. Does an ICMP reply prove the owner of an IPAM address? **No.** IPAM assignment is explicit inventory data.
4. Does the application generate a server-side PDF? **No.** Use browser print-to-PDF.
5. Is a topology map a full data backup? **No.** Use Backup and restore for a complete archive.
6. Can an Operator configure users or notification credentials? **No.** Those pages are Administrator-only.
7. Does a documentation screenshot's zero-device count describe production? **No.** The screenshots are empty-inventory UI examples.
8. Does code training guarantee live SNMP/Telegram/SMTP delivery? **No.** Validate real devices/providers in the deployment environment.

## 7. Completion and sign-off

| Trainee | Role | Date | Labs completed | Follow-up required | Trainer |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

Training completion means the participant completed the items appropriate to their role. It does not certify production readiness, network authorization, alert delivery, restore capability, or security approval. Production verification belongs in the [Production test checklist](../PRODUCTION_TEST_CHECKLIST.md).

## 8. Training references

- [User guide](./ASHPULSE_USER_GUIDE.md)
- [Administrator guide](./ASHPULSE_ADMINISTRATOR_GUIDE.md)
- [Implementation runbook](./ASHPULSE_IMPLEMENTATION_RUNBOOK.md)
- [Deployment guide](../DEPLOYMENT_GUIDE.md)
- [Documentation index](./ASHPULSE_DOCUMENTATION_INDEX.md)
