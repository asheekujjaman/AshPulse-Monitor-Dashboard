# Universal IT Monitor disaster recovery guide

This is an operator runbook, not evidence that a production restore or Windows Server migration has succeeded. Rehearse it with an isolated copy of a real backup before relying on it. Record approved Recovery Point Objective (RPO), Recovery Time Objective (RTO), data owners, certificate owners, and escalation contacts before deployment.

## What a full application backup contains

The Admin-only verified ZIP contains the persistent files below the configured JSON data directory, including device inventory, hierarchy, maps, IPAM, users, application/monitoring/backup/notification settings, report source history/samples, SNMP snapshots, logs, and the Data Protection key-ring files. The archive manifest records the file list, sizes, app version, backup time, and SHA-256 digests. Temporary locks and temporary files are excluded. The configured backup directory itself is separate from the archive's source tree.

The ZIP is **not encrypted**. Sensitive credential values inside JSON remain Data Protection ciphertext; inventory, history, report source data, logs, names, and other JSON are readable to anyone who can open the archive. Apply restrictive NTFS ACLs and encrypted off-host storage. SHA-256 detects accidental corruption but is not a signature and does not prove the archive came from a trusted operator. Store independent, access-controlled copies and keep offline/immutable copies against ransomware.

## Daily operations and pre-incident preparation

- Keep automatic backup enabled at the approved interval and retention in the Admin **Backup and restore** page. Check the backup activity result and file presence; a saved schedule is not proof a job ran.
- Create a manual verified backup before each release upgrade, Data Protection change, user/security change window, or planned migration.
- Copy completed archives to the approved off-host protected destination. Preserve at least one clean offline/immutable copy.
- Protect and separately back up the Data Protection key-encryption certificate private key and recovery instructions. The application ZIP contains the key-ring files, not the certificate PFX/private key or the source Windows DPAPI master-key context.
- Record exact release version, configuration paths, app pool name/identity, certificate thumbprints/expiry, ACLs, provider setup contacts, and approved restore owner in a restricted runbook.
- Monitor free space. JSONL monitoring history and sample files grow without an implemented retention/compaction feature. Do not delete or rotate active files as a workaround.
- Rehearse full restore to a separate path and confirm devices, maps, IPAM, users, monitoring history/samples, settings, and protected credentials before declaring the backup recoverable.

## Incident response and data preservation

1. Declare the incident, stop unauthorized changes, notify the incident owner, and identify a trusted backup based on the incident timeline. Preserve the existing application, logs, data, and backup copies for investigation.
2. If compromise/ransomware is suspected, isolate the host and backup share, preserve forensic copies, revoke exposed credentials, and use a clean rebuild host. Do not reconnect a potentially compromised write identity to immutable backups.
3. Do not overwrite or manually unzip a ZIP into the live data directory. Do not remove current files until the chosen restore archive has passed application validation and a safety copy of current data has been protected.
4. Record the chosen archive name, size, manifest/version/hash result, Data Protection key mode, target application version, restore operator, and approval.

## Normal Admin UI restore

Use this only if the application starts and the Admin Backup and Restore page can validate files.

1. Copy the trusted `uim-backup-*.zip` into the already configured backup directory. Preserve its exact file name and do not edit the ZIP/manifest.
2. Sign in as Admin over HTTPS. In **Backup and restore**, choose **Validate and preview**. Confirm app version compatibility, backup date, module list, files, and integrity. Stop if a required file/reference or key check fails.
3. Choose full or selected-module restore only if the preview and dependencies are correct. Select the explicit replacement confirmation. A full restore requires application restart; the service creates an automatic safety backup before swapping staged data and rolls back if the directory swap fails.
4. Wait for the application process to stop and the IIS pool/site to become available again. With `AlwaysRunning`/preload configured, IIS should start the app on demand/automatically; if not, start the pool/site according to the approved plan. Do not start a second process using the same data directory.
5. Verify Admin login, restored user state, dashboard inventory, current status, locations/groups, maps, IPAM, report history availability, notification settings presence (do not expose secret values), key access, new logs, and a fresh check/transition on a controlled device.
6. Check backup history for the safety backup and restore result. Create a new manual full backup after validation. Record actual time-to-recover and any missing historical interval.

## If the live application cannot start

The Admin UI restore cannot be used until a compatible app instance can initialize. Do not manually extract an archive over live data.

1. Stop the site and pool. Copy the full live data tree and relevant logs to a separate, access-restricted forensic/recovery location. Do not alter the only copy.
2. Verify the application release and Hosting Bundle, Production configuration, data/backup ACLs, app pool identity/profile, certificate thumbprint/private-key ACL, and a valid `.bak` recovery copy. Correct a deployment/configuration problem without discarding JSON or keys where possible.
3. If the production data tree is unusable, prepare a **separate recovery instance** using the same compatible app release and a new absolute data path outside its publish directory. Set its backup directory to a protected recovery folder containing a copy of the archive. Give the recovery process the correct Data Protection context/certificate and private-key access. Use a dedicated recovery site/process identity only if it can actually decrypt the archive's keys.
4. Start the isolated instance, perform first-run Admin setup if needed, preview the archive, then restore it through the app's validated full-restore workflow. Verify the restored system completely while still isolated.
5. For cutover, stop the affected production process and preserve its current state. Point the production configuration/site to the validated recovered data directory and approved external backup directory, using the supported same-host identity/key context. Confirm ACLs and certificate access, then start a single production instance. Keep the original tree offline and unchanged until sign-off.
6. If the archive's Data Protection keys cannot be opened, stop. Do not remove or rewrite encrypted credential fields. Recover the original compatible certificate/private key or Windows DPAPI context, or follow an approved separate credential-reissue plan that preserves ciphertext and documents which integrations need credentials to be entered again.

If no suitable application, runtime, keys, or compatible data copy is available, escalate to the system owner and application maintainer. There is no supported command-line ZIP extraction/recovery utility in this project.

## Data Protection keys and cross-server migration

### Same-server restore

On Windows Production without `DataProtection:CertificateThumbprint`, the app protects its key ring using current-user DPAPI and stores key files under `<data>\keys`. Preserve the same compatible Windows identity/profile/DPAPI master-key context and the complete key directory. The application pool should load its user profile. Copying only JSON data to another host or using another pool identity is not sufficient proof that old credentials can be decrypted.

### Planned certificate-based migration

For a planned new deployment, configure a dedicated certificate in `LocalMachine\My` **before** the app creates Data Protection keys or stores protected credentials:

1. Obtain a suitable private-key certificate through the organization's PKI/security process. Use an exportable protected backup copy for disaster recovery; protect the PFX password in the approved secret vault, not in this guide or source control.
2. Import the certificate into `Cert:\LocalMachine\My`, set `DataProtection:CertificateThumbprint` to its thumbprint, and grant the target app-pool identity Read access to its private key.
3. Verify the application starts and can create/read its protected key ring before entering notification/SNMP credentials. Then create credentials and perform a test backup and isolated credential-decryption restore.
4. For a second server, securely install the same certificate and private key in its `LocalMachine\My`; retain `SetApplicationName("UniversalITMonitor")`, copy the full key ring/data under the approved procedure, configure the same thumbprint, and grant the new identity private-key Read access.
5. Keep old Data Protection certificates and private keys available for as long as any persisted key-ring file needs them. Monitor certificate expiry and plan rotation with a restore rehearsal.

The existing application configures one protecting certificate and does not expose a certificate rotation/unprotect-with-old-cert list or rewrap tool. Changing the configured thumbprint does **not** re-encrypt old key-ring entries. Existing DPAPI-protected keys cannot be made portable merely by adding a certificate on the destination. For existing installations, retain the original DPAPI context or plan a separately reviewed credential migration; never silently discard encrypted values.

The Data Protection certificate is not an IIS HTTPS certificate. Back up both kinds of certificate/key material separately according to their roles and access policies.

## Windows Server migration sequence

1. Provision the target OS, IIS features, current supported .NET 10 Hosting Bundle, DNS/TLS certificate, dedicated pool/site, external data/backup locations, disk space, ACLs, firewall policy, and Data Protection recovery method. Follow [IIS_SETUP_CHECKLIST.md](./IIS_SETUP_CHECKLIST.md).
2. Publish the same or a reviewed compatible application version to a new release folder. Do not restore unknown data into a different app major version; the archive validator rejects unsupported future major versions but operator compatibility still needs review.
3. Stop source writes and create/verify a final full backup. Copy the ZIP, complete data/key directory (where needed), configuration values, release files, certificates/private keys via approved secure transfer, and operational metadata. Use a protected channel and verify independent file hashes for transferred artifacts.
4. Configure target paths and key protection; test restore in a separate target data path first. If source DPAPI is in use, confirm the required source context is actually available before proceeding. If it is not, do not expect notification or SNMP credentials to decrypt.
5. Verify restored inventory, login, maps/IPAM, history, logs, notification settings, protected credential operation, and a new backup. Update DNS/firewall only after cutover approval.
6. Keep the source offline and data read-only until target sign-off and the rollback period expire. Do not run source and target simultaneously against the same backup/data directories or give both processes the same data path.

## Recovery record

| Item | Value |
| --- | --- |
| Incident / change ID | ______________________________ |
| Source release / target release | ______________________________ |
| Selected archive and manifest result | ______________________________ |
| Source/target data path | ______________________________ |
| DPAPI context or DP certificate thumbprint | ______________________________ |
| Restore start/end (UTC) | ______________________________ |
| Safety backup file | ______________________________ |
| Inventory/history/key verification | ______________________________ |
| Actual RPO / RTO | ______________________________ |
| Missing data / credential follow-up | ______________________________ |
| Operator / approver | ______________________________ |
