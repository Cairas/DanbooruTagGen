@echo off
rem Build-only script for automated/repeated use (no pause, no auto-launch).
rem Rebuilding also refreshes data/presets/*.json in the output folder via
rem the csproj's CopyToOutputDirectory=PreserveNewest content items.
cd /d "%~dp0"
echo Building (Release)...
"C:\Program Files\dotnet\dotnet.exe" build -c Release -nologo
if errorlevel 1 (
  echo.
  echo Build failed. See errors above.
  exit /b 1
)
echo Build succeeded.
