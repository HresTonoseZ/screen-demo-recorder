@echo off
setlocal EnableExtensions EnableDelayedExpansion
title Screen Demo Recorder - Native Build

pushd "%~dp0"
echo Build started. Please wait...
echo.

set "REPOSITORY_USER_HOME=C:\Users\NRC_2"
set "DOTNET_CLI_HOME=%REPOSITORY_USER_HOME%"
set "NUGET_PACKAGES=%REPOSITORY_USER_HOME%\.nuget\packages"
set "NUGET_HTTP_CACHE_PATH=%REPOSITORY_USER_HOME%\AppData\Local\NuGet\v3-cache"
set "PSModuleAnalysisCachePath=%REPOSITORY_USER_HOME%\AppData\Local\Microsoft\Windows\PowerShell\ModuleAnalysisCache"
set "DOTNET_EXE=C:\Users\NRC_2\AppData\Local\Microsoft\dotnet\dotnet.exe"

if not exist "%DOTNET_EXE%" (
    echo Required .NET SDK launcher was not found:
    echo %DOTNET_EXE%
    echo.
    popd
    if not defined SDR_BUILD_NO_WAIT pause
    exit /b 1
)

echo Using .NET SDK: %DOTNET_EXE%
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-native.ps1" -DotNet "%DOTNET_EXE%" %*
set "BUILD_EXIT=%ERRORLEVEL%"
popd

echo.
if not "%BUILD_EXIT%"=="0" (
    echo Build failed with exit code %BUILD_EXIT%.
    echo.
    if not defined SDR_BUILD_NO_WAIT pause
) else (
    echo Build completed successfully.
    echo Executable: %~dp0dist\native-preview\ScreenDemoRecorder.exe
    echo.
    if not defined SDR_BUILD_NO_WAIT (
        echo This window will close automatically in 8 seconds.
        timeout /t 8 /nobreak >nul
    )
)

exit /b %BUILD_EXIT%
