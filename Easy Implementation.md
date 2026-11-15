# Easy Implementation

## AshPulse Infrastructure Monitor IIS Deployment

এই নির্দেশনা `site` folder-এর publish files ব্যবহার করে Windows Server-এর IIS-এ AshPulse চালু করার জন্য। `site` folder-এর ভেতরের সব contents IIS website-এর **Physical path**-এ কপি করবেন; `site` folder-টিকে আলাদা subfolder হিসেবে কপি করবেন না। Package-এ app-এর Windows x64 runtime এবং `web.config` আছে।

## একবারের server setup

1. Windows Server-এ IIS এবং **ASP.NET Core Hosting Bundle for .NET 10 (x64)** install করুন। Self-contained publish হলেও IIS-এর ASP.NET Core Module দরকার।
2. IIS-এ একটি Application Pool তৈরি করুন; **.NET CLR version: No Managed Code**, **Enable 32-Bit Applications: False**, **Load User Profile: True**, এবং **Maximum Worker Processes: 1** রাখুন।
3. Website তৈরি করে ওই Application Pool নির্বাচন করুন। DNS name, HTTPS binding এবং certificate সেট করুন।
4. Application Pool identity-কে website-এর physical folder-এ Modify permission দিন, যাতে app `App_Data`, logs, backups ও runtime files লিখতে পারে। উদাহরণ:

   ```powershell
   icacls 'C:\inetpub\wwwroot\AshPulse' /grant 'IIS AppPool\AshPulsePool:(OI)(CI)M'
   ```

5. Monitoring server থেকে device network-এ outbound ICMP এবং SNMP-এর UDP/161 যেতে পারে নিশ্চিত করুন। SNMP ব্যবহারের জন্য device-এর ACL/firewall-এ monitoring server-এর IP অনুমোদন করুন।

এই server setup একবারই করতে হয়। Server আগে থেকেই এভাবে প্রস্তুত থাকলে পরের deploy-এ শুধু `site` contents কপি করতে হবে।

## প্রথমবার deploy

1. Build machine-এ তৈরি `MonitoringDashboard/site` folder খুলুন।
2. `site`-এর ভেতরের সব file ও folder নির্বাচন করে IIS website-এর Physical path-এ copy করুন; চাইলে existing files replace করুন। `web.config` physical path-এর root-এ থাকতে হবে।
3. IIS Manager-এ website এবং Application Pool start করুন।
4. Browser-এ HTTPS address খুলুন। নতুন install হলে setup screen থেকে প্রথম Administrator account তৈরি করুন; preset password নেই।
5. Sign in করে একটি test device যোগ করুন। Ping-এর জন্য ICMP এবং SNMP metrics-এর জন্য device-এর SNMP version/credentials ও network access ঠিক আছে নিশ্চিত করুন।

## Upgrade deploy

1. Website বা Application Pool সাময়িকভাবে stop করুন।
2. বর্তমান IIS folder-এর `App_Data` এবং `Backups` backup করুন। এগুলোতে client-এর inventory, users, settings ও backup data থাকে।
3. নতুন `site` folder-এর contents IIS physical path-এ copy করে application files replace করুন। পুরোনো `App_Data` বা `Backups` মুছবেন না।
4. Application Pool এবং website start করে sign-in, device status, Ping ও SNMP metrics যাচাই করুন।

## চালু না হলে

- **HTTP 500.30:** ASP.NET Core Hosting Bundle x64 install আছে কি না এবং Application Pool identity-র write permission আছে কি না দেখুন। Windows Event Viewer-এর **Application** log-এ startup error দেখুন।
- **CSS/JavaScript পুরোনো:** Browser-এ hard refresh করুন; IIS folder-এ `wwwroot`-এর নতুন files কপি হয়েছে কি না দেখুন।
- **Device ping/SNMP পাওয়া যাচ্ছে না:** IIS server থেকে device network-এর route/firewall, ICMP, UDP/161 এবং device SNMP ACL/community বা v3 credentials যাচাই করুন।

## Package-এ কী আছে

- `UniversalITMonitor.Web.exe` এবং app-এর প্রয়োজনীয় .NET runtime files
- IIS-এর জন্য `web.config`
- `appsettings.json` এবং `wwwroot` static files

`appsettings.Development.json`, source code, `bin`/`obj`, PDB এবং local `App_Data` package-এ নেই।
