@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET SDK was not found. Install the .NET 10 SDK first.
  pause
  exit /b 1
)

dotnet publish ".\LocaleGameHub\LocaleGameHub.csproj" -c Release -r win-x64 --self-contained false -o ".\publish-small"
if errorlevel 1 (
  echo.
  echo Build failed.
  pause
  exit /b 1
)

echo.
echo Done: %CD%\publish-small
pause
