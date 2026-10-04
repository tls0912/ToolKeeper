@echo off
setlocal DisableDelayedExpansion
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start-TransLamp.ps1"
if errorlevel 1 (
    echo.
    pause
    exit /b 1
)
exit /b 0
