@echo off
setlocal

cd /d "%~dp0"

set "DOTNET_USER=%USERPROFILE%\.dotnet\dotnet.exe"
if exist "%DOTNET_USER%" (
  set "DOTNET_CMD=%DOTNET_USER%"
) else (
  set "DOTNET_CMD=dotnet"
)

echo Starting D2Helper...
echo Using: %DOTNET_CMD%
set "LOG_FILE=%TEMP%\d2helper-start.log"
echo Log: %LOG_FILE%
echo.

"%DOTNET_CMD%" run --project src\D2Helper.UI\D2Helper.UI.csproj -c Debug > "%LOG_FILE%" 2>&1
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
  echo.
  echo D2Helper exited with code %EXIT_CODE%.
  echo Last log lines:
  powershell -NoProfile -Command "Get-Content -Path '%LOG_FILE%' -Tail 40"
  echo Press any key to close.
  pause >nul
)

endlocal
