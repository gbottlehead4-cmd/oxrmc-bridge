@echo off
rem Build the live-rig tracing tools. Run with the .\ prefix:  .\build-tools.bat
rem Uses the .NET Framework C# compiler that ships with Windows (no SDK needed).
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

"%CSC%" /nologo /target:exe /out:SensorLogger.exe SensorLogger.cs
if %errorlevel% neq 0 ( echo SensorLogger build FAILED & exit /b 1 )

"%CSC%" /nologo /target:exe /out:SrtaSniffer.exe SrtaSniffer.cs
if %errorlevel% neq 0 ( echo SrtaSniffer build FAILED & exit /b 1 )

echo Built SensorLogger.exe and SrtaSniffer.exe
