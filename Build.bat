@echo off
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if exist GGShare.exe del GGShare.exe
if exist GGShare-Setup.exe del GGShare-Setup.exe

echo Building GGShare.exe ...
"%CSC%" /nologo /target:winexe /win32icon:app.ico /out:GGShare.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Security.dll /r:System.Web.Extensions.dll /resource:server.js,server.js /resource:index.html,index.html GGShare.cs
if not exist GGShare.exe goto fail

echo Building the installer ...
"%CSC%" /nologo /target:winexe /win32icon:app.ico /win32manifest:Setup.manifest /out:GGShare-Setup.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /resource:GGShare.exe,GGShare.exe Setup.cs
if not exist GGShare-Setup.exe goto fail

echo.
echo Done. Double-click GGShare-Setup.exe to install (this one file is all you need to share).
pause
exit /b 0

:fail
echo.
echo Build failed. Copy the error lines above and send them to me.
pause
exit /b 1
