@echo off
rem The panic button. Turns every category off, so links go back to the browser that was your
rem default before Browser Switch existed. Nothing is uninstalled and no category is lost.
"%~dp0BrowserSwitch.exe" --reset
echo.
echo No category is live. Links now open in your original default browser.
echo Nothing was removed - open "Switch browser" whenever you want a category back.
echo.
timeout /t 4 >nul
