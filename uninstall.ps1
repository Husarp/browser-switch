# Removes Browser Switch completely. Everything it created lives under HKEY_CURRENT_USER, so this
# puts the registry back exactly as it was - no leftovers.
#
# IMPORTANT, and the reason this script nags you: if Browser Switch is your default browser right
# now, set a real browser as default FIRST. Otherwise Windows is left pointing at something that no
# longer exists and will ask you to pick an app the next time you click a link.
#
# If you only want it to stop doing anything for a while, do NOT use this - double-click
# "Back to Firefox.cmd" instead. That leaves everything installed and ready to switch on again.

$ErrorActionPreference = 'Stop'
$app = 'BrowserSwitch'
$name = 'Browser Switch'

$current = (Get-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice' -ErrorAction SilentlyContinue).ProgId
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
  "HKCU:\Software\Clients\StartMenuInternet\$app"
)) { if (Test-Path $path) { Remove-Item $path -Recurse -Force } }

Remove-ItemProperty 'HKCU:\Software\RegisteredApplications' -Name $name -ErrorAction SilentlyContinue

$shortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Switch browser.lnk'
if (Test-Path $shortcut) { Remove-Item $shortcut -Force }

Write-Host ''
Write-Host 'Browser Switch has been removed from Windows.' -ForegroundColor Green
Write-Host 'The folder itself is untouched - run install.ps1 whenever you want it back.'
