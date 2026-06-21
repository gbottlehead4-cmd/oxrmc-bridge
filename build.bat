@echo off
echo Building OXRMC Bridge plugin...

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set SIMHUB=C:\Program Files (x86)\SimHub
set WPF=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF
set FW=C:\Windows\Microsoft.NET\Framework64\v4.0.30319

"%CSC%" /target:library /out:User.OXRMCBridge.dll ^
  /reference:"%SIMHUB%\SimHub.Plugins.dll" ^
  /reference:"%SIMHUB%\GameReaderCommon.dll" ^
  /reference:"%SIMHUB%\SimHub.Logging.dll" ^
  /reference:"%SIMHUB%\WoteverCommon.dll" ^
  /reference:"%SIMHUB%\log4net.dll" ^
  /reference:"%WPF%\PresentationCore.dll" ^
  /reference:"%WPF%\PresentationFramework.dll" ^
  /reference:"%WPF%\WindowsBase.dll" ^
  /reference:"%FW%\System.Xaml.dll" ^
  OXRMCBridgePlugin.cs SettingsControl.cs

if %errorlevel% equ 0 (
    echo Build successful: User.OXRMCBridge.dll
) else (
    echo Build FAILED
    pause
    exit /b 1
)
