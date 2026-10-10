@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Update-TestBuild.ps1" -Mode Launch
if errorlevel 1 pause
