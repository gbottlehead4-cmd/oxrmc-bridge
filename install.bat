@echo off
echo Installing OXRMC Bridge plugin to SimHub...

set SIMHUB=C:\Program Files (x86)\SimHub

if not exist User.OXRMCBridge.dll (
    echo ERROR: User.OXRMCBridge.dll not found. Run build.bat first.
    pause
    exit /b 1
)

copy /Y User.OXRMCBridge.dll "%SIMHUB%\User.OXRMCBridge.dll"

if %errorlevel% equ 0 (
    echo Installed successfully.
    echo Restart SimHub and enable "OXRMC Bridge" in Add/remove features.
) else (
    echo FAILED - is SimHub running? Close SimHub first, then retry.
)
pause
