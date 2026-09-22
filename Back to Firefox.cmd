@echo off
rem The panic button. Forces every link back to the personal browser without removing anything.
rem Use this if something looks wrong: it is instant, and "Switch browser" still works afterwards.
"%~dp0BrowserSwitch.exe" --set personal
echo.
echo All links now go to the personal browser (Firefox).
echo Nothing was uninstalled - double-click "Switch browser" whenever you want work mode back.
echo.
timeout /t 4 >nul
