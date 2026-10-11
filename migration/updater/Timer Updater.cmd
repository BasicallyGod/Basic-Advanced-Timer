@echo off
setlocal
set "TimerPowerShell=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%~dp0Timer-Updater-Window.ps1" (
    echo Timer-Updater-Window.ps1 is missing. Extract the complete updater folder first.
    pause
    exit /b 1
)
"%TimerPowerShell%" -NoLogo -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0Timer-Updater-Window.ps1" %*
if errorlevel 1 (
    echo Timer updater could not start. Read the error above and updater-startup-error.log beside this launcher.
    pause
    exit /b 1
)
exit /b 0
