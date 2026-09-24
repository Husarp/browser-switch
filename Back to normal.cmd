@echo off
rem The panic button. Every link goes back to the browser that was your default before Browser
rem Switch existed: no category is live any more, and rules are switched off, so they send nothing
rem elsewhere either. Nothing is uninstalled and no category or rule is lost.
"%~dp0BrowserSwitch.exe" --reset
echo.
echo Every link now opens in your original default browser.
echo Nothing was removed. To go back, open "Switch browser", pick a category
echo and tick "Use rules" (also in the dock's right-click menu).
echo.
timeout /t 4 >nul
