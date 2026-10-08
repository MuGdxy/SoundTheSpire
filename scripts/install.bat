@echo off
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/MuGdxy/SoundTheSpire/main/scripts/install.ps1 | iex"
echo.
pause
