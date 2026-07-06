@echo off
rem Double-click to watch what the bridge is writing into OXRMC's motionRigPose.
rem Run the bridge (run-bridge.bat) first. No admin needed here.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0pose-peek.ps1"
pause
