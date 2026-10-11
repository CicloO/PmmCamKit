@echo off
rem 用 Windows 自带的 .NET Framework 编译器重新编译
"%WINDIR%\Microsoft.NET\Framework644.0.30319\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /win32manifest:app.manifest /out:"..\PMM相机工具.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll PmmCore.cs PmmCameraFollow.cs
pause
