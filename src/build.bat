@echo off
rem Build PmmCamKit.exe with the C# compiler bundled in Windows (.NET Framework 4)
cd /d "%~dp0"
if not exist "..\build" mkdir "..\build"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /win32manifest:app.manifest /out:"..\build\PmmCamKit.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll Lang.cs PmmCore.cs PmmCamKit.cs
if errorlevel 1 (echo Build failed.) else (echo Built: ..\build\PmmCamKit.exe)
pause
