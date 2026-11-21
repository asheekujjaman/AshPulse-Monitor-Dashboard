# AshPulse IIS Deployment: Full Process

**Server:** Windows Server 2022 (not domain-joined)
**Site folder:** `C:\inetpub\wwwroot\AshPulse`
**App Pool:** `AshPulsePool`
**Website:** `AshPulse`
**Host name:** `ashpulse.local` (change if you want)

Run all commands in PowerShell as Administrator.

---

## Step 1: Install IIS

```powershell
Install-WindowsFeature -Name Web-Server, Web-Mgmt-Console, Web-Scripting-Tools -IncludeManagementTools
```

Check:

```powershell
Get-WindowsFeature Web-Server
```

It should show `Installed`.

## Step 2: Install .NET 10 Hosting Bundle (x64)

Download **ASP.NET Core Hosting Bundle for .NET 10** from `https://dotnet.microsoft.com/download/dotnet/10.0` (Windows > Hosting Bundle). Then install:

```powershell
.\dotnet-hosting-10.0.x-win.exe /install /quiet /norestart
iisreset
```

(Replace `10.0.x` with the real file name.)

Check:

```powershell
dotnet --list-runtimes
Get-WebGlobalModule | Where-Object Name -eq "AspNetCoreModuleV2"
```

## Step 3: Copy application files

Copy everything **inside** the `site` folder into `C:\inetpub\wwwroot\AshPulse`. Do not copy the `site` folder itself.

```powershell
New-Item -ItemType Directory -Force C:\inetpub\wwwroot\AshPulse | Out-Null
# Example (if site folder is in C:\Temp\site):
Copy-Item C:\Temp\site\* C:\inetpub\wwwroot\AshPulse -Recurse -Force
```

Check:

```powershell
Test-Path C:\inetpub\wwwroot\AshPulse\web.config
Test-Path C:\inetpub\wwwroot\AshPulse\UniversalITMonitor.Web.exe
```

Both must return `True`.

## Step 4: Create App Pool

```powershell
Import-Module WebAdministration

New-WebAppPool -Name "AshPulsePool"

$p = "IIS:\AppPools\AshPulsePool"
Set-ItemProperty $p managedRuntimeVersion ""                 # No Managed Code
Set-ItemProperty $p enable32BitAppOnWin64 $false             # 64-bit
Set-ItemProperty $p processModel.loadUserProfile $true       # Load User Profile
Set-ItemProperty $p processModel.maxProcesses 1              # 1 worker process
Set-ItemProperty $p startMode AlwaysRunning                  # Always running
Set-ItemProperty $p processModel.idleTimeout "00:00:00"      # Never idle-stop
```

Check:

```powershell
Get-ItemProperty $p | Select managedRuntimeVersion, enable32BitAppOnWin64, startMode
Get-ItemProperty $p -Name processModel | Select loadUserProfile, maxProcesses
```

## Step 5: Folder permission

The app writes to `App_Data`, `Backups` and logs, so the app pool identity needs Modify permission.

```powershell
New-Item -ItemType Directory -Force C:\inetpub\wwwroot\AshPulse\App_Data, C:\inetpub\wwwroot\AshPulse\Backups | Out-Null
icacls 'C:\inetpub\wwwroot\AshPulse' /grant 'IIS AppPool\AshPulsePool:(OI)(CI)M'
```

## Step 6: Create Website with HTTPS

Because the server is not domain-joined, use a self-signed certificate (or import one from your CA).

```powershell
Stop-Website "Default Web Site"

$cert = New-SelfSignedCertificate -DnsName "ashpulse.local" `
  -CertStoreLocation Cert:\LocalMachine\My -NotAfter (Get-Date).AddYears(2)

New-Website -Name "AshPulse" `
  -PhysicalPath "C:\inetpub\wwwroot\AshPulse" `
  -ApplicationPool "AshPulsePool" `
  -Port 443 -Ssl -HostHeader "ashpulse.local" -SslFlags 1

(Get-WebBinding -Name "AshPulse" -Protocol https).AddSslCertificate($cert.Thumbprint, "My")
```

If you have a CA-issued certificate, import it to `Cert:\LocalMachine\My` and use its thumbprint instead of the self-signed one.

## Step 7: Firewall

```powershell
New-NetFirewallRule -DisplayName "AshPulse HTTPS In" -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow
New-NetFirewallRule -DisplayName "AshPulse SNMP Out" -Direction Outbound -Protocol UDP -RemotePort 161 -Action Allow
New-NetFirewallRule -DisplayName "AshPulse ICMP Out" -Direction Outbound -Protocol ICMPv4 -Action Allow
```

## Step 8: Start and test

```powershell
Start-WebAppPool AshPulsePool
Start-Website AshPulse

Get-WebAppPoolState AshPulsePool
Get-Website AshPulse
Invoke-WebRequest https://ashpulse.local -UseBasicParsing | Select StatusCode
```

Open `https://ashpulse.local` in a browser. Client PCs need a DNS record or a `hosts` file entry:

```
<server-ip>  ashpulse.local
```

On first run, create the first Administrator account from the setup screen. Then add a test device to check Ping and SNMP.

---

## Troubleshooting

- **HTTP 500.30:** Hosting Bundle (x64) missing, or the app pool identity has no Modify permission. Check Event Viewer > Windows Logs > Application.
- **HTTP 404 / blank page:** Files copied into a `site` subfolder by mistake. `web.config` must be directly inside `AshPulse`.
- **"Not secure" in browser:** Expected with a self-signed certificate. Use a CA-issued certificate to remove the warning.
- **No ping/SNMP data:** Check route and firewall to the device network, ICMP, UDP/161, and the device SNMP ACL/credentials.

## Upgrade (later)

```powershell
Stop-Website AshPulse; Stop-WebAppPool AshPulsePool
# Backup App_Data and Backups first, then copy new site contents (do not delete App_Data or Backups)
Start-WebAppPool AshPulsePool; Start-Website AshPulse
```


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
