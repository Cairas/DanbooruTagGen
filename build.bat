@echo off
cd /d "%~dp0"
echo Building (Release)...
dotnet build -c Release -nologo
if errorlevel 1 (
  echo.
  echo Build failed. See errors above.
  pause
  exit /b 1
)
start "" "src\DanbooruTagGen.App\bin\Release\net9.0-windows\DanbooruTagGen.App.exe"
