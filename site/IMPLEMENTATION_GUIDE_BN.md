# AshPulse IIS Implementation Guide (বাংলা)

এই নির্দেশিকা Windows Server-এর IIS-এ `site` folder-এর published files চালু করার জন্য। Server, IIS, firewall, DNS, certificate এবং বাস্তব device connectivity আলাদা করে যাচাই করতে হবে; শুধু copy করলেই production readiness নিশ্চিত হয় না।

## ১. Server প্রস্তুত করুন

1. Supported ও patched Windows Server ব্যবহার করুন। IIS Role যোগ করার সময় **Web Server**, **Static Content**, **Default Document**, **HTTP Errors**, **Request Filtering** এবং **IIS Management Console** রাখুন।
2. IIS ইনস্টল হওয়ার পরে সর্বশেষ supported **.NET 10 ASP.NET Core Hosting Bundle (x64)** install করুন। শুধু .NET SDK install করলেই IIS hosting প্রস্তুত হয় না। Bundle install/repair-এর পরে প্রয়োজন হলে IIS বা server restart করুন।
3. Server-এর DNS name, HTTPS certificate, site path এবং application-pool name ঠিক করুন। উদাহরণ: `monitor.example.com`, `C:\inetpub\wwwroot\AshPulse`, `AshPulsePool`।
4. App server থেকে approved device network-এ ICMP এবং SNMP-এর জন্য UDP/161 outbound যেতে পারে কি না firewall/network team দিয়ে নিশ্চিত করুন। Telegram/SMTP চালালে নির্ধারিত provider/relay-তে প্রয়োজনীয় outbound HTTPS/SMTP-ও খুলুন।

## ২. `site` files IIS folder-এ copy করুন

1. IIS-এ website-এর **Physical path** হিসেবে একটি নির্দিষ্ট folder দিন, যেমন `C:\inetpub\wwwroot\AshPulse`। Folder না থাকলে তৈরি করুন।
2. এই package-এর `site` folder খুলে ভেতরের **সব file ও folder-এর contents** IIS physical path-এ copy করুন। `site` folder-টিকেই আরেকটি subfolder হিসেবে paste করবেন না।
3. নিশ্চিত করুন `web.config` এবং `UniversalITMonitor.Web.dll` IIS physical path-এর root-এ আছে। IIS-এর Physical path-ও এই একই folder হতে হবে।
4. `bin`, `obj`, source code বা developer-এর local data copy করবেন না। `appsettings.Development.json` publish output-এ থাকলেও IIS-এর Production environment ব্যবহার করুন।
5. Upgrade হলে copy করার আগে site/app pool stop করুন, বর্তমান release ও `App_Data`/`Backups` backup নিন, এবং পুরোনো `App_Data` ও `Backups` মুছবেন না। New build copy-তে ওই data folder না থাকলেও আগের live folder সংরক্ষণ করুন।

## ৩. Production host configuration দিন

IIS folder-এর `appsettings.json` খুলে `AllowedHosts`-এ browser দিয়ে যে DNS name ব্যবহার করবেন সেটি লিখুন। Wildcard `*` production-এ রেখে দেবেন না। উদাহরণ:

```json
"AllowedHosts": "monitor.example.com"
```

একাধিক নির্দিষ্ট host অনুমোদন করতে semicolon দিয়ে লিখুন, যেমন `monitor.example.com;monitor`। JSON-এর comma/quote ঠিক আছে কি না save করার আগে যাচাই করুন। Default storage রাখলে `Storage:DataDirectory` হবে `App_Data`; অন্য কোনো path না চাইলে এটি পরিবর্তন করবেন না।

## ৪. IIS Application Pool ও permission সেট করুন

IIS Manager → **Application Pools** → **Add Application Pool**:

- Name: `AshPulsePool` (নিজের নাম ব্যবহার করতে পারেন)
- .NET CLR version: **No Managed Code**
- Managed pipeline: **Integrated**
- Advanced Settings → **Enable 32-Bit Applications: False**
- Advanced Settings → **Load User Profile: True**
- **Maximum Worker Processes: 1**
- Monitoring/automatic backup সবসময় চালু রাখতে **Start Mode: AlwaysRunning**, **Idle Time-out: 0** এবং IIS **Application Initialization / Preload Enabled** configure করুন।

Site-এর **Basic Settings**-এ এই pool নির্বাচন করুন। Default `App_Data` path-এ application data, logs, Data Protection keys, process lock, restore staging এবং default `Backups` site folder-এর নিচে তৈরি হয়। তাই elevated PowerShell-এ pool name/path মিলিয়ে application-pool identity-কে site folder-এ Modify দিন:

```powershell
icacls 'C:\inetpub\wwwroot\AshPulse' /grant 'IIS AppPool\AshPulsePool:(OI)(CI)M'
```

শুধু প্রয়োজনীয় folder-এ permission দিন; `Everyone` বা `Users`-কে Modify দেবেন না। Site আলাদা version folder-এ রাখলে stable external data path configure করে সেই path-এও pool identity-কে Modify দিন।

## ৫. HTTPS site তৈরি ও চালু করুন

1. IIS Manager → **Sites** → **Add Website**-এ site name, application pool, Physical path এবং host name দিন।
2. HTTPS binding যোগ করুন—সঠিক host name, port `443`, trusted TLS certificate এবং প্রয়োজন হলে SNI নির্বাচন করুন। Certificate-এর DNS name browser URL-এর সঙ্গে মিলতে হবে এবং client computer-এ issuing CA trusted হতে হবে।
3. Production site-এ untrusted network থেকে HTTP access বন্ধ/redirect করুন। App-এ built-in MFA নেই; external access-এর আগে অনুমোদিত MFA-protected gateway বা সমমানের access control দিন।
4. Application pool ও site Start করুন। Client browser-এ `https://monitor.example.com` খুলুন।

## ৬. প্রথম Admin account তৈরি করুন

নতুন data folder হলে sign-in পেজ first-run setup-এ পাঠাবে। Display name দিন; username শুরুতে `admin` থাকে; কমপক্ষে ১২ অক্ষরের শক্ত password তৈরি করে নিরাপদ password manager-এ রাখুন। প্রথম account তৈরি হলে setup বন্ধ হয়ে যাবে এবং পরেরবার sign-in page আসবে। কোনো preset admin password নেই।

Upgrade-এ পুরোনো `App_Data` রাখা হলে first-run setup আবার আসবে না—আগের administrator account ব্যবহার করুন।

## ৭. Cisco/MikroTik SNMP যাচাই করুন

1. Cisco switch বা MikroTik-এ approved **read-only SNMP** configuration দিন। SNMP community/user-কে শুধু monitoring server-এর source IP থেকে query করতে অনুমতি দিন। Version, credentials এবং SNMP view/device policy নেটওয়ার্ক administrator-এর সঙ্গে মিলিয়ে নিন; credential সাধারণ file বা email-এ পাঠাবেন না।
2. Firewall-এ monitoring server থেকে device-এর SNMP UDP/161 request/response অনুমোদিত রাখুন। SNMP v1/v2c বা v3—device ও application-এ একই version/credential দিতে হবে।
3. App-এ device edit করে SNMP enable করুন, সঠিক version, port (সাধারণত 161), read-only credential এবং প্রয়োজনে SNMPv3 security fields দিন। Device details-এ **Test SNMP**-ийг চালিয়ে success/error message যাচাই করুন।
4. Cisco switch-এ IF-MIB interface counters, interface status, VLAN info; MikroTik-এ supported interface counters/details—target device-এ বাস্তবে যাচাই করুন। Cisco VLAN readout-এর জন্য switch SNMP policy/view-তে Q-BRIDGE/BRIDGE MIB access প্রয়োজন হতে পারে। কোনো OID/MIB device firmware বা configuration-এ প্রকাশ না হলে app সেটি দেখাতে পারবে না।
5. Device details-এর interface table-এ port name, admin/operational status, VLAN (agent দিলে), এবং RX/TX rate দেখুন। Counter থেকে rate পেতে অন্তত দুইটি poll sample প্রয়োজন হতে পারে। Unneeded interface **Hide** করলে সেই interface UI-তে লুকাবে; পরে **Show** করে ফেরত আনা যায়।

## ৮. Go-live checklist

- [ ] IIS folder root-এ `web.config`, `UniversalITMonitor.Web.dll`, `.deps.json`, `.runtimeconfig.json`, settings এবং `wwwroot` আছে।
- [ ] x64 .NET 10 ASP.NET Core Hosting Bundle installed; Application Pool `No Managed Code`, 64-bit, one worker, Load User Profile enabled।
- [ ] `AllowedHosts`-এ actual production host; IIS URL HTTPS এবং certificate trusted।
- [ ] Application-pool identity default data layout-এর site folder-এ Modify পায়; `App_Data`/`Backups` preserved (upgrade হলে)।
- [ ] First Admin setup/sign-in সফল; আলাদা role/user access যাচাই করা হয়েছে।
- [ ] একটি approved test device-এর ICMP polling ও status যাচাই; প্রয়োজন হলে Cisco ও MikroTik দুটিতে SNMP Test, port status, VLAN এবং live RX/TX sample যাচাই।
- [ ] Manual backup তৈরি হয়েছে, archive preview/validation হয়েছে, এবং off-server backup owner/plan নির্ধারিত।
- [ ] Application pool recycle/restart-এর পর site, monitoring ও logs সচল—target server-এ যাচাই করা হয়েছে।

## ৯. Error হলে কোথায় দেখবেন

প্রথমে IIS site ও pool Started কি না, Physical path সঠিক কি না, `web.config` root-এ আছে কি না, Hosting Bundle/ANCM install হয়েছে কি না এবং ACL ঠিক আছে কি না পরীক্ষা করুন। এরপর:

- **HTTP 500.19**: IIS/ANCM configuration বা `web.config` সমস্যা হতে পারে। Hosting Bundle install/repair এবং IIS restart যাচাই করুন।
- **HTTP 500.30 / 502.5**: app startup/runtime failure হতে পারে। Windows **Event Viewer → Windows Logs → Application** এবং `App_Data\logs` দেখুন।
- **HTTP 403**: binding, host restriction (`AllowedHosts`) অথবা NTFS read/execute permission যাচাই করুন।
- **SNMP timeout/empty interfaces**: device IP/UDP 161 reachability, SNMP version/credential, source-IP ACL/read-only view, device firmware ও MIB support যাচাই করুন। Browser/server restart সাধারণত SNMP firewall block ঠিক করে না।
- **Data write / access denied**: `IIS AppPool\AshPulsePool` (আপনার pool name অনুযায়ী) site/data folder-এ Modify পায় কি না দেখুন।

ANCM stdout logging কেবল troubleshooting-এর জন্য সাময়িকভাবে enable করুন; protected log path ও ACL নিশ্চিত করে issue মিটলে আবার disable এবং diagnostic logs সরিয়ে ফেলুন। Application-এর persisted logs default path-এ `App_Data\logs`-এর নিচে থাকে।

## ১০. Upgrade-এর সময় data রাখুন

1. App-এর **Backup and restore** থেকে full backup তৈরি ও preview/validation করুন; সম্ভব হলে off-server copy রাখুন।
2. IIS site এবং pool stop করুন; old worker বন্ধ হওয়া পর্যন্ত অপেক্ষা করুন।
3. পুরোনো site folder, `App_Data`, `Backups`, `appsettings.json` এবং release identifier-এর backup নিন।
4. নতুন published contents copy করুন, কিন্তু live `App_Data`/`Backups` এবং environment-specific settings replace/delete করবেন না।
5. Pool/site চালু করে sign-in, monitoring, SNMP (ব্যবহার করলে), logs ও backup history যাচাই করুন। সমস্যা হলে site stop করে reviewed rollback plan অনুসরণ করুন। শুধু পুরোনো DLL ফেরত দিলে data rollback হয় না।

Deploy-এর আগে server-specific DNS, certificate, access control, firewall এবং backup plan-এর দায়িত্ব নির্দিষ্ট operator দিয়ে যাচাই করান। Target server-এর checklist পূরণ না হওয়া পর্যন্ত deployment-কে production-verified বলা যাবে না।
