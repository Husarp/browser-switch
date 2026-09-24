@echo off
rem Rebuilds BrowserSwitch.exe using the C# compiler that ships inside Windows.
rem Nothing is downloaded and nothing needs to be installed.
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ ^
  /out:"%~dp0BrowserSwitch.exe" /win32icon:"%~dp0BrowserSwitch.ico" ^
  /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ^
  "%~dp0BrowserSwitch.cs" "%~dp0SwitchForm.cs" "%~dp0Tray.cs" "%~dp0Shortcuts.cs" ^
  "%~dp0Rules.cs" "%~dp0Ui.cs" "%~dp0FileIcon.cs"
if errorlevel 1 (echo BUILD FAILED & pause & exit /b 1)
echo Built BrowserSwitch.exe
