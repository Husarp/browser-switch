# Removes Browser Switch completely. Everything it created lives under HKEY_CURRENT_USER, so this
# puts the registry back exactly as it was - no leftovers.
#
# IMPORTANT, and the reason this script nags you: if Browser Switch is your default browser right
# now, set a real browser as default FIRST. Otherwise Windows is left pointing at something that no
# longer exists and will ask you to pick an app the next time you click a link.
#
# If you only want it to stop doing anything for a while, do NOT use this - double-click
# "Back to normal.cmd" instead. That leaves everything installed and ready to switch on again.

$ErrorActionPreference = 'Stop'
$app = 'BrowserSwitch'
$name = 'Browser Switch'

# Windows 11 24H2 and later record the choice in UserChoiceLatest and can leave the older UserChoice
# unchanged, so the newer record wins whenever it exists.
$assoc = 'HKCU:\SOFTWARE\Microsoft\Windows\Shell\Associations\UrlAssociations\http'
$current = (Get-ItemProperty "$assoc\UserChoiceLatest\ProgId" -ErrorAction SilentlyContinue).ProgId
if (-not $current) { $current = (Get-ItemProperty "$assoc\UserChoice" -ErrorAction SilentlyContinue).ProgId }
if ($current -like 'BrowserSwitch*') {
  Write-Host ''
  Write-Host 'Browser Switch is currently your default browser.' -ForegroundColor Yellow
  Write-Host 'Set Firefox (or Chrome) as default in Settings > Apps > Default apps first,'
  Write-Host 'then run this again. Opening that page for you now.'
  Start-Process 'ms-settings:defaultapps'
  return
}

foreach ($path in @(
  "HKCU:\Software\Classes\BrowserSwitchURL",
  "HKCU:\Software\Clients\StartMenuInternet\$app",
  "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$app"
)) { if (Test-Path $path) { Remove-Item $path -Recurse -Force } }

Remove-ItemProperty 'HKCU:\Software\RegisteredApplications' -Name $app -ErrorAction SilentlyContinue
Remove-ItemProperty 'HKCU:\Software\RegisteredApplications' -Name $name -ErrorAction SilentlyContinue  # the name used before 2.1.0

foreach ($shortcut in @(
  (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Switch browser.lnk'),
  (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Browser Switch.lnk'),
  (Join-Path ([Environment]::GetFolderPath('Startup')) 'Browser Switch.lnk')
)) { if (Test-Path $shortcut) { Remove-Item $shortcut -Force } }

# the dock keeps running in the background until told to stop
Get-Process BrowserSwitch -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Browser Switch has been removed from Windows.' -ForegroundColor Green
Write-Host 'The folder itself is untouched - run install.ps1 whenever you want it back.'
