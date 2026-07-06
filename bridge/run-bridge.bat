@echo off
rem Launch the Sigma->OXRMC bridge. Double-click; it self-elevates (raw-socket
rem sniff needs admin) and stays open so you can read output / Ctrl+C.
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting administrator rights...
    powershell -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
cd /d "%~dp0"
echo === Sigma -> OXRMC bridge (elevated) ===
echo Do NOT run the SimHub OXRMC Bridge plugin at the same time (both write the
echo same motionRigPose file). Drive a game; in VR, CTRL+INS activate, CTRL+DEL
echo calibrate with the rig level. Ctrl+C here to stop.
echo(
"%~dp0SigmaOxrmcBridge.exe"
echo(
pause
