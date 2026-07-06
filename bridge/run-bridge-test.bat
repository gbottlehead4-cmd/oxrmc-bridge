@echo off
rem EXAGGERATED test build: compensation x4 so the on/off difference in VR is
rem obvious. Use this only to confirm the chain works, then use run-bridge.bat
rem (gain 1) for real driving.
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting administrator rights...
    powershell -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
cd /d "%~dp0"
set BRIDGE_GAIN=4
echo === Sigma -> OXRMC bridge  (TEST, gain x%BRIDGE_GAIN%) ===
echo Close the SimHub plugin first. In VR: CTRL+INS activate, CTRL+DEL calibrate
echo level. Then brake / corner - the compensation should be very obvious now.
echo Toggle CTRL+INS on/off to compare. Ctrl+C to stop.
echo(
"%~dp0SigmaOxrmcBridge.exe"
echo(
pause
