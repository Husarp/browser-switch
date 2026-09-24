# Removes Browser Switch completely. Everything it created lives under HKEY_CURRENT_USER, so this
# puts the registry back exactly as it was - no leftovers.
#
# If Browser Switch is your default browser right now, the default has to go back to a real browser
# first - otherwise Windows is left pointing at something that no longer exists. Windows lets only
# you make that choice, so this opens Settings straight on the page of the browser you used before
# Browser Switch (remembered at install time): one click on "Set default" there. It waits for that
# and then carries on by itself.
#
# If you only want it to stop doing anything for a while, do NOT use this - double-click
# "Back to normal.cmd" instead. That leaves everything installed and ready to switch on again.
#
# -CheckOnly: say what would happen, change nothing.

param([switch]$CheckOnly)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$app = 'BrowserSwitch'
$name = 'Browser Switch'

# Windows 11 24H2 and later record the choice in UserChoiceLatest and can leave the older UserChoice
# unchanged, so the newer record wins whenever it exists.
function Get-DefaultBrowser {
  $assoc = 'HKCU:\SOFTWARE\Microsoft\Windows\Shell\Associations\UrlAssociations\https'
  $id = (Get-ItemProperty "$assoc\UserChoiceLatest\ProgId" -ErrorAction SilentlyContinue).ProgId
  if (-not $id) { $id = (Get-ItemProperty "$assoc\UserChoice" -ErrorAction SilentlyContinue).ProgId }
  return $id
}

# The browser used before Browser Switch: its program was written into config.txt at install time
# ("fallback="). Windows' Settings page for it is found through the list of registered browsers.
function Find-PreviousBrowser {
  $config = Join-Path $here 'config.txt'
  $exe = $null
  if (Test-Path $config) {
    $line = Get-Content $config | Where-Object { $_ -like 'fallback=*' } | Select-Object -First 1
    if ($line) { $exe = $line.Substring(9).Trim() }
  }
  if (-not $exe) { return $null }
  foreach ($hive in 'HKCU:', 'HKLM:') {
    $registered = Get-Item "$hive\Software\RegisteredApplications" -ErrorAction SilentlyContinue
    if (-not $registered) { continue }
    foreach ($id in $registered.GetValueNames()) {
      $capabilities = [string]$registered.GetValue($id)
      if ($id -like 'BrowserSwitch*' -or $capabilities -notlike '*StartMenuInternet*') { continue }   # browsers only
      $client = Split-Path $capabilities -Parent
      $command = (Get-ItemProperty "$hive\$client\shell\open\command" -ErrorAction SilentlyContinue).'(default)'
      if ($command -and $command.ToLowerInvariant().Contains($exe.ToLowerInvariant())) {
        $label = (Get-ItemProperty "$hive\$client" -ErrorAction SilentlyContinue).'(default)'
        if (-not $label) { $label = $id }
        return @{ Id = $id; Name = $label; Exe = $exe }
      }
    }
  }
  return @{ Id = $null; Name = [IO.Path]::GetFileNameWithoutExtension($exe); Exe = $exe }
}

$current = Get-DefaultBrowser
if ($current -like 'BrowserSwitch*') {
  $previous = Find-PreviousBrowser
  $page = if ($previous -and $previous.Id) { "ms-settings:defaultapps?registeredAppUser=$($previous.Id)" } else { 'ms-settings:defaultapps' }
  if ($CheckOnly) {
    Write-Host "Browser Switch is the default browser. Would open $page"
    if ($previous) { Write-Host "  - the page of $($previous.Name) ($($previous.Exe)), to press Set default there" }
    else { Write-Host '  - the Default apps page: no browser from before Browser Switch was remembered' }
  } else {
    Write-Host ''
    if ($previous) {
      Write-Host "First, $($previous.Name) becomes your default browser again - the one you used before." -ForegroundColor Yellow
      Write-Host "Settings is opening on its page: press  Set default  at the top."
    } else {
      Write-Host 'First, choose the browser that should be your default from now on.' -ForegroundColor Yellow
      Write-Host 'Settings is opening: choose a browser and press  Set default.'
    }
    Write-Host 'Windows lets only you make this choice. This window waits, then finishes by itself.'
    Start-Process $page
    $until = (Get-Date).AddMinutes(5)
    while ((Get-DefaultBrowser) -like 'BrowserSwitch*' -and (Get-Date) -lt $until) { Start-Sleep -Seconds 1 }
    if ((Get-DefaultBrowser) -like 'BrowserSwitch*') {
      Write-Host ''
      Write-Host 'Browser Switch is still the default browser, so nothing was removed. Run this again when' -ForegroundColor Yellow
      Write-Host 'another browser is the default.'
      return
    }
    Write-Host 'Done - the default browser is back. Removing Browser Switch...' -ForegroundColor Green
  }
}

$keys = @("HKCU:\Software\Classes\BrowserSwitchURL",
          "HKCU:\Software\Clients\StartMenuInternet\$app",
          "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$app")
$shortcuts = @((Join-Path ([Environment]::GetFolderPath('Desktop')) 'Switch browser.lnk'),
               (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Browser Switch.lnk'),
               (Join-Path ([Environment]::GetFolderPath('Startup')) 'Browser Switch.lnk'))
# a copy installed by the one-command installer lives in its own folder, which goes too; any other
# folder - one you unzipped or built yourself - is left as it is
$ownFolder = Join-Path $env:LOCALAPPDATA 'Programs\Browser Switch'
$removeFolder = ($here.TrimEnd('\') -eq $ownFolder.TrimEnd('\')) -and -not (Test-Path (Join-Path $here '.git'))

if ($CheckOnly) {
  Write-Host 'Would remove:'
  $keys | Where-Object { Test-Path $_ } | ForEach-Object { Write-Host "  $_" }
  Write-Host "  HKCU:\Software\RegisteredApplications > $app"
  $shortcuts | Where-Object { Test-Path $_ } | ForEach-Object { Write-Host "  $_" }
  Write-Host ('  the folder ' + $here + $(if ($removeFolder) { '' } else { ' - no, it is left as it is' }))
  return
}

# the dock keeps running in the background until told to stop
Get-Process BrowserSwitch -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

foreach ($path in $keys) { if (Test-Path $path) { Remove-Item $path -Recurse -Force } }
Remove-ItemProperty 'HKCU:\Software\RegisteredApplications' -Name $app -ErrorAction SilentlyContinue
Remove-ItemProperty 'HKCU:\Software\RegisteredApplications' -Name $name -ErrorAction SilentlyContinue  # the name used before 2.1.0
foreach ($shortcut in $shortcuts) { if (Test-Path $shortcut) { Remove-Item $shortcut -Force } }

Write-Host ''
Write-Host 'Browser Switch has been removed from Windows.' -ForegroundColor Green
if ($removeFolder) {
  Start-Sleep -Milliseconds 500
  Set-Location $env:TEMP
  Remove-Item $here -Recurse -Force -ErrorAction SilentlyContinue
  Write-Host 'Its folder is gone too.'
} else {
  Write-Host 'The folder itself is untouched - run install.ps1 whenever you want it back.'
}
