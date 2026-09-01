@echo off
setlocal EnableExtensions EnableDelayedExpansion
title Screen Demo Recorder - Build

set "SDK_VERSION=10.0.400"
set "APP_TOOL_DIR=%LOCALAPPDATA%\ScreenDemoRecorder"
set "APP_DOTNET_DIR=%APP_TOOL_DIR%\dotnet"
set "DOTNET_EXE="

echo Checking build requirements...
where powershell.exe >nul 2>&1
if errorlevel 1 (
    echo.
    echo Missing requirement: Windows PowerShell.
    echo PowerShell is included with supported Windows versions and is required by this build.
    goto :failed
)

call :try_dotnet "%APP_DOTNET_DIR%\dotnet.exe"
call :try_dotnet "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
call :try_dotnet "%ProgramFiles%\dotnet\dotnet.exe"

if not defined DOTNET_EXE (
    echo.
    echo Missing requirement: .NET SDK %SDK_VERSION% x64.
    choice /c YN /n /m "Download and install it for the current user now? [Y/N]: "
    if errorlevel 2 goto :cancelled

    echo.
    echo Installing .NET SDK %SDK_VERSION% into:
    echo %APP_DOTNET_DIR%
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install-dotnet-sdk.ps1" -Version "%SDK_VERSION%" -InstallDir "%APP_DOTNET_DIR%"
    set "INSTALL_EXIT=%ERRORLEVEL%"
    if not "!INSTALL_EXIT!"=="0" (
        echo .NET SDK installation failed with exit code !INSTALL_EXIT!.
        goto :failed
    )

    call :try_dotnet "%APP_DOTNET_DIR%\dotnet.exe"
    if not defined DOTNET_EXE (
        echo .NET SDK installation completed, but the required SDK could not be verified.
        goto :failed
    )
)

echo Found .NET SDK %SDK_VERSION%:
echo %DOTNET_EXE%
echo.
echo Building Screen Demo Recorder...

set "DOTNET_CLI_HOME=%APP_TOOL_DIR%"
set "NUGET_PACKAGES=%APP_TOOL_DIR%\NuGet\packages"
set "NUGET_HTTP_CACHE_PATH=%APP_TOOL_DIR%\NuGet\v3-cache"
set "PSModuleAnalysisCachePath=%APP_TOOL_DIR%\PowerShell\ModuleAnalysisCache"

pushd "%~dp0"
call :shutdown_build_servers
echo Preparing a clean output folder...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\prepare-native-output.ps1" -OutputDirectory "%~dp0dist\native-preview"
set "BUILD_EXIT=%ERRORLEVEL%"
if "%BUILD_EXIT%"=="0" (
    "%DOTNET_EXE%" publish "native\src\ScreenDemoRecorder\ScreenDemoRecorder.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o "dist\native-preview" --nologo --disable-build-servers
    set "BUILD_EXIT=!ERRORLEVEL!"
)
call :shutdown_build_servers
popd
cd /d "%TEMP%"

echo.
if not "%BUILD_EXIT%"=="0" (
    echo Build failed with exit code %BUILD_EXIT%.
    pause
    exit /b %BUILD_EXIT%
)

echo Build completed successfully.
echo Executable: %~dp0dist\native-preview\ScreenDemoRecorder.exe
echo.
pause
exit /b 0

:try_dotnet
if defined DOTNET_EXE exit /b 0
if not exist "%~1" exit /b 0
if not exist "%~dp1sdk\%SDK_VERSION%\dotnet.dll" exit /b 0
set "DOTNET_EXE=%~1"
exit /b 0

:shutdown_build_servers
call :shutdown_one "%APP_DOTNET_DIR%\dotnet.exe"
call :shutdown_one "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
call :shutdown_one "%ProgramFiles%\dotnet\dotnet.exe"
exit /b 0

:shutdown_one
if not exist "%~1" exit /b 0
"%~1" build-server shutdown >nul 2>&1
exit /b 0

:cancelled
echo.
echo Installation cancelled. The application was not built.
goto :failed

:failed
echo.
cd /d "%TEMP%"
pause
exit /b 1
