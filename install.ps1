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
$app    = 'BrowserSwitch'
$name   = 'Browser Switch'

function Set-Key($path, $value, $propertyName = '(default)') {
  if (-not (Test-Path $path)) { New-Item -Path $path -Force | Out-Null }
  if ($null -ne $value) { New-ItemProperty -Path $path -Name $propertyName -Value $value -PropertyType String -Force | Out-Null }
}

# 1. the handler: what to run when something opens a link
Set-Key "HKCU:\Software\Classes\$progId" $name
Set-Key "HKCU:\Software\Classes\$progId" '' 'URL Protocol'
Set-Key "HKCU:\Software\Classes\$progId\DefaultIcon" "$exe,0"
Set-Key "HKCU:\Software\Classes\$progId\shell\open\command" "`"$exe`" `"%1`""

# 2. the entry Settings reads, so the app can be offered as a browser at all
$client = "HKCU:\Software\Clients\StartMenuInternet\$app"
Set-Key $client $name
Set-Key "$client\DefaultIcon" "$exe,0"
Set-Key "$client\shell\open\command" "`"$exe`""
Set-Key "$client\Capabilities" $name 'ApplicationName'
Set-Key "$client\Capabilities" 'Sends links to Firefox or Chrome, whichever mode is switched on' 'ApplicationDescription'
Set-Key "$client\Capabilities" "$exe,0" 'ApplicationIcon'
foreach ($scheme in 'http', 'https') { Set-Key "$client\Capabilities\URLAssociations" $progId $scheme }
foreach ($ext in '.htm', '.html')     { Set-Key "$client\Capabilities\FileAssociations" $progId $ext }
Set-Key "$client\Capabilities\StartMenu" $app 'StartMenuInternet'

# 3. tell Windows this registration exists
Set-Key 'HKCU:\Software\RegisteredApplications' "Software\Clients\StartMenuInternet\$app\Capabilities" $name

# 4. a shortcut on the Desktop that flips the mode - this is the one click
$shortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Switch browser.lnk'
$wsh = New-Object -ComObject WScript.Shell
$lnk = $wsh.CreateShortcut($shortcut)
$lnk.TargetPath = $exe
$lnk.Arguments = '--toggle'
$lnk.WorkingDirectory = $here
$lnk.IconLocation = "$exe,0"
$lnk.Description = 'Flip links between the personal browser and the work browser'
$lnk.Save()

# 5. start off in personal mode, so behaviour is identical to now until you flip it
if (-not (Test-Path (Join-Path $here 'mode.txt'))) { 'personal' | Set-Content (Join-Path $here 'mode.txt') -NoNewline }

Write-Host ''
Write-Host 'Browser Switch is registered.' -ForegroundColor Green
Write-Host ''
Write-Host 'One step left, and it has to be you - Windows does not let a script do it:'
Write-Host '  1. Open Settings > Apps > Default apps'
Write-Host '  2. Find "Browser Switch" and set it as the default for HTTP and HTTPS'
Write-Host ''
Write-Host 'Then double-click "Switch browser" on your Desktop to flip between Firefox and Chrome.'
Write-Host 'To undo everything: run uninstall.ps1'
