@echo off
title LOCAL-IA Launcher - VS Code Copilot
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run-LocalIA.ps1"
pause
