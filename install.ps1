# Makes "Browser Switch" appear in Windows Settings as a browser you can choose.
#
# This does NOT change your default browser. It only adds Browser Switch to the list, the same way
# installing a browser does. Nothing about your browsing changes until you pick it yourself in
# Settings - Windows requires that click and will not accept it from a script.
#
# Everything is written under HKEY_CURRENT_USER, so no administrator rights are needed and nothing
# is touched for other accounts on this computer. uninstall.ps1 removes every key this creates.

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe  = Join-Path $here 'BrowserSwitch.exe'
if (-not (Test-Path $exe)) { throw "BrowserSwitch.exe is missing - run build.cmd first." }

$progId = 'BrowserSwitchURL'
$app    = 'BrowserSwitch'     # the client key name AND the name in RegisteredApplications - Firefox
                              # and Brave use the same token for both, so this does too
$name   = 'Browser Switch'    # what a person sees
$about  = 'Sends each link to the browser and profile you chose'

function Set-Key($path, $value, $propertyName = '(default)') {
  if (-not (Test-Path $path)) { New-Item -Path $path -Force | Out-Null }
  if ($null -ne $value) { New-ItemProperty -Path $path -Name $propertyName -Value $value -PropertyType String -Force | Out-Null }
}

# 1. the handler: what to run when something opens a link
Set-Key "HKCU:\Software\Classes\$progId" $name
Set-Key "HKCU:\Software\Classes\$progId" '' 'URL Protocol'
Set-Key "HKCU:\Software\Classes\$progId\DefaultIcon" "$exe,0"
Set-Key "HKCU:\Software\Classes\$progId\shell\open\command" "`"$exe`" `"%1`""

# ...and WHO that handler is. Without this, Windows' own list of apps for http links did include
# Browser Switch - first, marked "recommended" - but under the name "BrowserSwitch.exe", with no
# icon, no publisher and no app identity. Brave's handler carries exactly these values, and Brave
# is registered per-user the same way we are. AppUserModelId matches the Start menu shortcut and
# the client key, so Windows can tell all three are the same program.
Set-Key "HKCU:\Software\Classes\$progId" $app 'AppUserModelId'
Set-Key "HKCU:\Software\Classes\$progId\Application" $app 'AppUserModelId'
Set-Key "HKCU:\Software\Classes\$progId\Application" $name 'ApplicationName'
Set-Key "HKCU:\Software\Classes\$progId\Application" "$exe,0" 'ApplicationIcon'
Set-Key "HKCU:\Software\Classes\$progId\Application" $about 'ApplicationDescription'
Set-Key "HKCU:\Software\Classes\$progId\Application" $name 'ApplicationCompany'

# 2. the entry Settings reads, so the app can be offered as a browser at all
$client = "HKCU:\Software\Clients\StartMenuInternet\$app"
Set-Key $client $name
Set-Key "$client\DefaultIcon" "$exe,0"
Set-Key "$client\shell\open\command" "`"$exe`""
Set-Key "$client\Capabilities" $name 'ApplicationName'
Set-Key "$client\Capabilities" $about 'ApplicationDescription'
Set-Key "$client\Capabilities" "$exe,0" 'ApplicationIcon'
foreach ($scheme in 'http', 'https') { Set-Key "$client\Capabilities\URLAssociations" $progId $scheme }
foreach ($ext in '.htm', '.html')     { Set-Key "$client\Capabilities\FileAssociations" $progId $ext }
Set-Key "$client\Capabilities\StartMenu" $app 'StartMenuInternet'

# No InstallInfo key, which browser installers also write: its commands are ones Windows may run on
# its own, and nothing here needs them.

# 3. tell Windows this registration exists. The value name matches the client key name, the way
#    browser installers do it.
Remove-ItemProperty 'HKCU:\Software\RegisteredApplications' -Name $name -ErrorAction SilentlyContinue
Set-Key 'HKCU:\Software\RegisteredApplications' "Software\Clients\StartMenuInternet\$app\Capabilities" $app

# 4. two shortcuts that open the window: one in the Start menu, as every installed browser has, and
#    one on the Desktop.
$wsh = New-Object -ComObject WScript.Shell
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Browser Switch.lnk'
$desktop   = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Switch browser.lnk'
foreach ($shortcut in @($startMenu, $desktop)) {
  $lnk = $wsh.CreateShortcut($shortcut)
  $lnk.TargetPath = $exe
  # set explicitly: CreateShortcut opens the existing file if there is one, so a property left alone
  # keeps whatever it had before - which is how an old "--toggle" survived a reinstall
  $lnk.Arguments = ''
  $lnk.WorkingDirectory = $here
  $lnk.IconLocation = "$exe,0"
  $lnk.Description = 'Choose which browser and profile your links open in'
  $lnk.Save()
}

# 4a. Start with Windows - in the dock (notification area) only, no window: --tray. Deleting this
#     shortcut from the Startup folder stops that; nothing else depends on it.
$startup = Join-Path ([Environment]::GetFolderPath('Startup')) 'Browser Switch.lnk'
$lnk = $wsh.CreateShortcut($startup)
$lnk.TargetPath = $exe
$lnk.Arguments = '--tray'
$lnk.WorkingDirectory = $here
$lnk.IconLocation = "$exe,0"
$lnk.Description = 'Browser Switch in the notification area'
$lnk.Save()

# 4b. Give the Start menu shortcut an "AppUserModelID" - the short identity string Windows uses to
#     tell one application from another. Browsers set one on their shortcuts ('Chrome', 'MSEdge');
#     it is the same ID the link handler carries (step 1), so Windows can see that the shortcut,
#     the handler and the registration are one program.
#
#     There is no way to set this from WScript.Shell, so this uses the shortcut's property store
#     directly. The C# below is compiled by the compiler already inside Windows - same as
#     build.cmd - so nothing is downloaded.
if (-not ('BsShortcutIdentity' -as [type])) {
  Add-Type -Language CSharp @'
using System;
using System.Runtime.InteropServices;

public static class BsShortcutIdentity
{
    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string f, int mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string f, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string f);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string f);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        void GetCount(out uint c);
        void GetAt(uint i, out PROPERTYKEY k);
        void GetValue(ref PROPERTYKEY k, IntPtr pv);
        void SetValue(ref PROPERTYKEY k, IntPtr pv);
        void Commit();
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] class ShellLink { }

    [DllImport("ole32.dll")] static extern int PropVariantClear(IntPtr pv);

    public static void Set(string shortcutPath, string id)
    {
        object link = new ShellLink();
        ((IPersistFile)link).Load(shortcutPath, 2);           // STGM_READWRITE
        PROPERTYKEY key = new PROPERTYKEY();
        key.fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");   // PKEY_AppUserModel_ID
        key.pid = 5;
        IntPtr pv = Marshal.AllocCoTaskMem(24);               // one PROPVARIANT
        for (int i = 0; i < 24; i++) Marshal.WriteByte(pv, i, 0);
        Marshal.WriteInt16(pv, 0, 31);                        // VT_LPWSTR
        Marshal.WriteIntPtr(pv, 8, Marshal.StringToCoTaskMemUni(id));
        IPropertyStore store = (IPropertyStore)link;
        store.SetValue(ref key, pv);
        store.Commit();
        ((IPersistFile)link).Save(shortcutPath, true);
        PropVariantClear(pv);
        Marshal.FreeCoTaskMem(pv);
    }
}
'@
}
[BsShortcutIdentity]::Set($startMenu, $app)

# 4c. Register as an installed program - the record that lists it in Settings > Apps > Installed
#     apps, so it can be removed from there like any other program. Per user, like the rest, so no
#     administrator rights are needed.
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\BrowserSwitch"
if (-not (Test-Path $uninstallKey)) { New-Item -Path $uninstallKey -Force | Out-Null }
$uninstallCmd = "`"$PSHOME\powershell.exe`" -NoProfile -ExecutionPolicy Bypass -File `"$here\uninstall.ps1`""
Set-Key $uninstallKey $name 'DisplayName'
Set-Key $uninstallKey "$exe,0" 'DisplayIcon'
Set-Key $uninstallKey '2.7.7' 'DisplayVersion'
Set-Key $uninstallKey $here 'InstallLocation'
Set-Key $uninstallKey $uninstallCmd 'UninstallString'
New-ItemProperty -Path $uninstallKey -Name 'NoModify' -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'NoRepair' -Value 1 -PropertyType DWord -Force | Out-Null

# 4d. Tell Windows that associations changed. This is the documented last step for any installer
#     that registers file or link handlers: without it, parts of Windows keep using what they read
#     earlier until the next sign-in. (SHCNE_ASSOCCHANGED = 0x08000000.)
if (-not ('BsInstall.Shell' -as [type])) {
  Add-Type -Namespace BsInstall -Name Shell -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("shell32.dll")]
public static extern void SHChangeNotify(int eventId, uint flags, System.IntPtr item1, System.IntPtr item2);
'@
}
[BsInstall.Shell]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)

# 5. Remember which browser is the default RIGHT NOW, before anything changes. With no categories
#    set up yet, links keep going exactly where they went before instead of to a browser we picked.
$config = Join-Path $here 'config.txt'
if (-not (Test-Path $config)) {
  # the newer UserChoiceLatest record wins when it exists (Windows 11 24H2 and later)
  $assoc  = 'HKCU:\SOFTWARE\Microsoft\Windows\Shell\Associations\UrlAssociations\http'
  $progId = (Get-ItemProperty "$assoc\UserChoiceLatest\ProgId" -ErrorAction SilentlyContinue).ProgId
  if (-not $progId) { $progId = (Get-ItemProperty "$assoc\UserChoice" -ErrorAction SilentlyContinue).ProgId }
  $fallback = $null
  if ($progId -and $progId -notlike 'BrowserSwitch*') {
    $cmd = (Get-ItemProperty "HKCU:\Software\Classes\$progId\shell\open\command" -ErrorAction SilentlyContinue).'(default)'
    if (-not $cmd) { $cmd = (Get-ItemProperty "HKLM:\SOFTWARE\Classes\$progId\shell\open\command" -ErrorAction SilentlyContinue).'(default)' }
    if ($cmd -match '"([^"]+\.exe)"') { $fallback = $Matches[1] }
  }
  $lines = @('# Browser Switch. Edit by hand if you like - the window writes the same thing.',
             '# category=<name>|<browser exe>|<profile arguments>|<what to show>', '', 'active=')
  if ($fallback) { $lines += "fallback=$fallback"; Write-Host "Current default browser remembered as the fallback: $fallback" }
  $lines -join "`r`n" | Set-Content $config -Encoding UTF8
}

Write-Host ''
Write-Host 'Browser Switch is registered.' -ForegroundColor Green
Write-Host ''
Write-Host 'One step left, and it has to be you - Windows does not let a script do it:'
Write-Host '  1. Open Settings > Apps > Default apps'
Write-Host '  2. Find "Browser Switch" and set it as the default for HTTP and HTTPS'
Write-Host ''
Write-Host 'Then double-click "Switch browser" on your Desktop. The window lists every browser on this'
Write-Host 'computer with its profiles; make the categories you want and point each one at a profile.'
Write-Host 'Until you do, links keep going where they go today.'
Write-Host 'To undo everything: run uninstall.ps1'
