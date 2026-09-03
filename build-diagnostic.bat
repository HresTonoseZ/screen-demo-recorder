@echo off
setlocal EnableExtensions
title Screen Demo Recorder - Diagnostic Build
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-diagnostic.ps1" %*
set "BUILD_EXIT=%ERRORLEVEL%"
cd /d "%TEMP%"
if not "%BUILD_EXIT%"=="0" echo Diagnostic build failed with exit code %BUILD_EXIT%.
if not defined SDR_BUILD_NO_PAUSE pause
exit /b %BUILD_EXIT%
