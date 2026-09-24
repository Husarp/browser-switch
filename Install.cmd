@echo off
rem Registers Browser Switch with Windows, for your account only. No administrator rights needed.
rem
rem Double-click this yourself. Registry changes made from inside some programs - including AI
rem coding assistants - go into a private copy of the registry that Windows Settings never reads,
rem so Browser Switch would look installed to them and not exist to Windows.
rem
rem PowerShell is called by its full address, not by name: on a PC whose PATH has lost the
rem WindowsPowerShell folder, "powershell.exe" alone is "not recognized".
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
echo.
pause
