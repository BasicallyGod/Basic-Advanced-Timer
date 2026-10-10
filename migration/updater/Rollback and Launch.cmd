@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Update-TestBuild.ps1" -Mode Rollback
if errorlevel 1 pause
