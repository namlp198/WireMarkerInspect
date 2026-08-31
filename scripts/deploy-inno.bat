@echo off
setlocal
set "VERSION=%~1"
if "%VERSION%"=="" set "VERSION=0.1.0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0verify-release.ps1" -Version "%VERSION%" -RequireOcrAssets
exit /b %ERRORLEVEL%
