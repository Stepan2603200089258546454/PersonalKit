@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0AgentsTools\Start-Docker.ps1" -Interactive
set "EXITCODE=%ERRORLEVEL%"

echo.
if "%EXITCODE%"=="0" (
    echo Done.
) else (
    echo Build or start failed. Exit code: %EXITCODE%
    echo See the messages above.
)
echo.
pause
exit /b %EXITCODE%
