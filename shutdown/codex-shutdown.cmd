@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "DELAY=%~1"
if not defined DELAY set "DELAY=60"

echo(!DELAY!| findstr /r "^[0-9][0-9]*$" >nul
if errorlevel 1 (
    echo Usage: codex-shutdown.cmd [seconds]
    echo Example: codex-shutdown.cmd 60
    exit /b 2
)

shutdown /s /t !DELAY!
