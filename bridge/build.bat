@echo off
rem Build the standalone Sigma->OXRMC bridge. Run with the .\ prefix: .\build.bat
rem In-box .NET Framework C# compiler (no SDK needed).
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
"%CSC%" /nologo /target:exe /out:"%~dp0SigmaOxrmcBridge.exe" "%~dp0SigmaOxrmcBridge.cs"
if %errorlevel% neq 0 ( echo BUILD FAILED & exit /b 1 )
echo Built SigmaOxrmcBridge.exe
