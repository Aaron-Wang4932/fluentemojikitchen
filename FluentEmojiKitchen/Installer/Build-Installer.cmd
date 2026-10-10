@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
set "buildExit=%ERRORLEVEL%"
echo.
if not "%buildExit%"=="0" echo Build failed. See the error above.
pause
exit /b %buildExit%
