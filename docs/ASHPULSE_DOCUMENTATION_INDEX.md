# AshPulse Infrastructure Monitor — Documentation

This documentation set describes the application implemented in this repository. It separates implemented behavior from deployment checks that must still be performed in the target environment.

## Start here

| Audience | Document | Use it for |
| --- | --- | --- |
| All signed-in users | [User guide](./ASHPULSE_USER_GUIDE.md) | Navigate the dashboard, manage permitted inventory, read statuses, work with maps/IPAM/reports, and troubleshoot common issues. |
| System administrators | [Administrator guide](./ASHPULSE_ADMINISTRATOR_GUIDE.md) | Configure accounts, monitoring, SNMP, alerts, themes, backups, access, and operations. |
| Deployment engineers | [Implementation runbook](./ASHPULSE_IMPLEMENTATION_RUNBOOK.md) | Plan, publish, configure, validate, hand over, upgrade, and roll back an IIS deployment. |
| Client deployment | [Easy Implementation](../Easy%20Implementation.md) | Build the `site` folder and copy its contents into the IIS website folder with the required one-time IIS setup. |
| Trainers and trainees | [Training guide](./ASHPULSE_TRAINING_GUIDE.md) | Deliver a guided class with role-based labs and a completion checklist. |

## Engineering and operational references

- [Deployment guide](../DEPLOYMENT_GUIDE.md) — detailed Windows Server/IIS deployment, configuration, permissions, upgrade, and rollback procedures.
- [IIS setup checklist](../IIS_SETUP_CHECKLIST.md) — target-server checklist. A checkbox is evidence only after an operator has completed and recorded the check.
- [Production test checklist](../PRODUCTION_TEST_CHECKLIST.md) — operational checks for monitoring, notifications, SNMP, backups, restore, IIS recycle, and responsive UI.
- [Disaster recovery guide](../DISASTER_RECOVERY_GUIDE.md) — backup restore, data protection keys, and migration constraints.
- [Architecture and recovery](./ARCHITECTURE.md) — source-backed architecture, storage format, monitoring flow, roles, and known limits.
- [Architecture and feature inventory](./ASHPULSE_ARCHITECTURE_AND_UI_INVENTORY.md) — project inspection and UI feature inventory.
- [Brand and UI reference](./ASHPULSE_BRAND_AND_UI.md) — AshPulse brand assets and design system.
- [Project status](../PROJECT_STATUS.md) — latest recorded engineering verification and unverified integrations.

## Product scope at a glance

AshPulse is an on-premises ASP.NET Core MVC application. The current implementation stores data in local JSON/JSONL files; it does not use a database server. It supports local accounts, device inventory, scheduled ICMP monitoring, a multi-target live Ping page, optional read-only SNMP collection, locations/groups, topology maps, IPAM subnet discovery with Add/Update actions, reports, Telegram/SMTP notification configuration, and administrator-managed backups and restores.

The application does not include Active Directory authentication, built-in MFA, Excel import/export, server-side PDF generation, DHCP/IP configuration, or multi-node shared storage. Live SNMP and external Telegram/SMTP delivery must be verified with the target devices and provider credentials before relying on them.

## Screenshot note

The screenshots in [`screenshots/`](./screenshots/) show the implemented interface with an empty inventory. Their zero counts and unavailable history are illustrative empty-state values, not production monitoring data.
