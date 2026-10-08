@echo off
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/MuGdxy/SoundTheSpire/main/scripts/uninstall.ps1 | iex"
echo.
pause
