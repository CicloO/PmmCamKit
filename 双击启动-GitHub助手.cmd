@echo off
setlocal
set "SCRIPT=%~dp0GitHub-Repo-Helper.ps1"
if not exist "%SCRIPT%" (
  echo Missing file: GitHub-Repo-Helper.ps1
  echo Please keep the CMD and PS1 files in the same project folder.
  pause
  exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%"
if errorlevel 1 (
  echo.
  echo Script exited with an error. Please check PowerShell output.
  pause
)
endlocal
