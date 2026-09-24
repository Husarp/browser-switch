# Browser Switch - install or update with one command, in PowerShell:
#
#     irm https://raw.githubusercontent.com/Husarp/browser-switch/main/get.ps1 | iex
#
# What it does, in order:
#   1. asks GitHub which release is the newest, and downloads that release's source code;
#   2. builds BrowserSwitch.exe from it with the C# compiler that is part of Windows - no ready-made
#      .exe is downloaded, so what runs is exactly the code you can read on GitHub;
#   3. puts it in %LOCALAPPDATA%\Programs\Browser Switch - for your account only, no admin rights;
#   4. registers it with Windows (install.ps1) and starts it. The first time, it opens on the setup
#      screen, which walks you through the one step Windows leaves to you.
#
# Run it again to update: your categories, rules and settings are kept. The app's own Update button
# runs this same script. To remove Browser Switch: Settings > Apps > Installed apps.
#
# Environment variables that change where things come from and go:
#   BROWSERSWITCH_DIR         install here instead
#   BROWSERSWITCH_UPDATE      "1": this is an update - start in the background afterwards, no window
#   BROWSERSWITCH_SOURCE      testing: a .zip of the source on this PC, instead of the newest release
#   BROWSERSWITCH_NOREGISTER  testing: "1" builds and copies only - stops, registers and starts nothing

& {
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'   # the progress bar makes downloads many times slower
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

    $repo   = 'Husarp/browser-switch'
    $dir    = if ($env:BROWSERSWITCH_DIR) { $env:BROWSERSWITCH_DIR } else { Join-Path $env:LOCALAPPDATA 'Programs\Browser Switch' }
    $update = $env:BROWSERSWITCH_UPDATE -eq '1'
    $temp   = Join-Path ([IO.Path]::GetTempPath()) ('BrowserSwitch-' + [guid]::NewGuid().ToString('N').Substring(0, 8))

    function Say($text, $colour = 'Gray') { Write-Host $text -ForegroundColor $colour }

    # A copy made from a clone of the code (it has a .git folder) is someone's working copy: updating
    # it from a release would overwrite their unfinished work.
    if (Test-Path (Join-Path $dir '.git')) {
        Say "$dir is a development copy (a git clone). Update it with git pull and build.cmd instead." 'Yellow'
        return
    }

    $csc = @("$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
             "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $csc) {
        Say 'The C# compiler that comes with Windows (.NET Framework 4) was not found, so Browser Switch cannot be built here.' 'Red'
        Say "Download the ready-made zip instead: https://github.com/$repo/releases/latest"
        return
    }

    New-Item -ItemType Directory -Force $temp | Out-Null
    try {
        # 1. the source code of the newest release
        $zip = Join-Path $temp 'source.zip'
        if ($env:BROWSERSWITCH_SOURCE) {
            Say "Using the source in $env:BROWSERSWITCH_SOURCE"
            Copy-Item $env:BROWSERSWITCH_SOURCE $zip
        } else {
            $release = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" -Headers @{ 'User-Agent' = 'BrowserSwitch-get' }
            Say "Downloading Browser Switch $($release.tag_name) (source code)..."
            Invoke-WebRequest $release.zipball_url -OutFile $zip -UseBasicParsing -Headers @{ 'User-Agent' = 'BrowserSwitch-get' }
        }
        Expand-Archive $zip (Join-Path $temp 'x') -Force
        # GitHub puts everything in one folder named after the repository and commit
        $src = Get-ChildItem (Join-Path $temp 'x') -Recurse -Filter 'BrowserSwitch.cs' | Select-Object -First 1 | ForEach-Object { $_.DirectoryName }
        if (-not $src) { throw 'The download does not contain Browser Switch.' }

        # 2. build it - into the temporary folder, so a failed build leaves an installed copy as it was
        Say 'Building it with the C# compiler that is part of Windows...'
        $sources = Get-ChildItem $src -Filter '*.cs' | ForEach-Object { $_.FullName }
        $built = Join-Path $temp 'BrowserSwitch.exe'
        $out = & $csc /nologo /target:winexe /optimize+ "/out:$built" "/win32icon:$(Join-Path $src 'BrowserSwitch.ico')" `
                      /reference:System.Windows.Forms.dll /reference:System.Drawing.dll $sources
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $built)) { $out | ForEach-Object { Say "  $_" 'Red' }; throw 'The build failed.' }

        # 3. into place. A running Browser Switch holds its program file, and one from another folder
        #    would stay in charge of the dock, so any that runs is stopped first. Your own files -
        #    config.txt, the link log, the list of apps - are never touched.
        New-Item -ItemType Directory -Force $dir | Out-Null
        $exe = Join-Path $dir 'BrowserSwitch.exe'
        $testing = $env:BROWSERSWITCH_NOREGISTER -eq '1'
        if (-not $testing) {
            Get-Process BrowserSwitch -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 700
        }
        Get-ChildItem $dir -Filter '*.cs' | Remove-Item -Force   # source of the old version, some of it maybe gone since
        foreach ($f in Get-ChildItem $src -File) {
            if ($f.Name -in 'config.txt', 'link-log.txt', 'recent-apps.txt', '.gitignore', '.gitattributes') { continue }
            Copy-Item $f.FullName (Join-Path $dir $f.Name) -Force
        }
        Copy-Item $built $exe -Force
        $version = (Get-Item $exe).VersionInfo
        Say ("Browser Switch {0}.{1}.{2} is in $dir" -f $version.FileMajorPart, $version.FileMinorPart, $version.FileBuildPart) 'Green'

        # 4. register it with Windows and start it
        if ($testing) { Say '(testing: not registered, not started)'; return }
        # In a PowerShell of its own that may run it: pasted into a normal PowerShell window, this
        # command runs, but Windows' default policy blocks script files such as install.ps1.
        # (install.ps1 of releases before 3.8.0 does not know -Quiet)
        $install = Join-Path $dir 'install.ps1'
        $go = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $install)
        if ((Get-Content $install -Raw) -match '\[switch\]\$Quiet') { $go += '-Quiet' }
        & (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') @go
        if ($LASTEXITCODE -ne 0) { throw 'Registering Browser Switch with Windows failed - see above.' }
        if ($update) { Start-Process $exe -ArgumentList '--tray' } else { Start-Process $exe }
        Say ''
        if ($update) { Say 'Updated. Browser Switch is running again.' 'Green' }
        else { Say 'Installed. Browser Switch is open - it walks you through the last step.' 'Green' }
    }
    finally {
        Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
