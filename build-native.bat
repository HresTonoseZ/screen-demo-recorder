@echo off
setlocal EnableExtensions EnableDelayedExpansion
title Screen Demo Recorder - Build

set "SDK_VERSION=10.0.400"
set "APP_TOOL_DIR=%LOCALAPPDATA%\ScreenDemoRecorder"
set "APP_DOTNET_DIR=%APP_TOOL_DIR%\dotnet"
set "DOTNET_EXE="
set "REPO_DIR=%~dp0."

:check_update
if /i "%~1"=="--skip-update-check" goto :after_update_check
call :check_repository_update
set "UPDATE_RESULT=!ERRORLEVEL!"
if "!UPDATE_RESULT!"=="100" exit /b 0
if "!UPDATE_RESULT!"=="101" exit /b 1

:after_update_check
if defined SDR_UPDATE_CHECK_ONLY exit /b 0

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
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\prepare-native-output.ps1" -OutputDirectory "%~dp0dist\screen-demo-recorder"
set "BUILD_EXIT=%ERRORLEVEL%"
if "%BUILD_EXIT%"=="0" (
    "%DOTNET_EXE%" publish "native\src\ScreenDemoRecorder\ScreenDemoRecorder.csproj" -c Release -r win-x64 --self-contained true -p:RecorderDiagnostics=false -p:PublishSingleFile=false -o "dist\screen-demo-recorder" --nologo --disable-build-servers
    set "BUILD_EXIT=!ERRORLEVEL!"
)
call :shutdown_build_servers
popd
cd /d "%TEMP%"

echo.
if not "%BUILD_EXIT%"=="0" (
    echo Build failed with exit code %BUILD_EXIT%.
    if not defined SDR_BUILD_NO_PAUSE pause
    exit /b %BUILD_EXIT%
)

echo Build completed successfully.
echo Executable: %~dp0dist\screen-demo-recorder\ScreenDemoRecorder.exe
echo.
if not defined SDR_BUILD_NO_PAUSE pause
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

:check_repository_update
where git.exe >nul 2>&1
if errorlevel 1 (
    echo Git was not found. Continuing with the local source.
    exit /b 0
)
git -C "%REPO_DIR%" rev-parse --is-inside-work-tree >nul 2>&1
if errorlevel 1 (
    echo This folder is not a Git working tree. Continuing with the local source.
    exit /b 0
)
git -C "%REPO_DIR%" remote get-url origin >nul 2>&1
if errorlevel 1 (
    echo The origin remote is not configured. Continuing with the local source.
    exit /b 0
)

echo Checking for repository updates...
git -C "%REPO_DIR%" fetch --quiet origin +refs/heads/main:refs/remotes/origin/main
if errorlevel 1 (
    echo The update check could not reach origin. Continuing with the local source.
    exit /b 0
)

for /f "delims=" %%H in ('git -C "%REPO_DIR%" rev-parse HEAD') do set "LOCAL_COMMIT=%%H"
for /f "delims=" %%H in ('git -C "%REPO_DIR%" rev-parse origin/main') do set "REMOTE_COMMIT=%%H"
for /f "delims=" %%H in ('git -C "%REPO_DIR%" rev-parse --short^=8 HEAD') do set "LOCAL_COMMIT_SHORT=%%H"
for /f "delims=" %%H in ('git -C "%REPO_DIR%" rev-parse --short^=8 origin/main') do set "REMOTE_COMMIT_SHORT=%%H"
echo Local:  !LOCAL_COMMIT_SHORT!
echo Server: !REMOTE_COMMIT_SHORT!
if "!LOCAL_COMMIT!"=="!REMOTE_COMMIT!" (
    echo The local repository is up to date.
    exit /b 0
)

git -C "%REPO_DIR%" merge-base --is-ancestor HEAD origin/main >nul 2>&1
if errorlevel 1 (
    git -C "%REPO_DIR%" merge-base --is-ancestor origin/main HEAD >nul 2>&1
    if not errorlevel 1 (
        echo The local repository contains commits that are not on the server. Building locally.
    ) else (
        echo The local and server histories have diverged. Automatic update is unsafe; building locally.
    )
    exit /b 0
)

choice /c YN /n /m "A newer version is available. Download it before building? [Y/N]: "
if errorlevel 2 (
    echo Update skipped. Building the local version.
    exit /b 0
)

git -C "%REPO_DIR%" diff --quiet --ignore-submodules --
if errorlevel 1 (
    echo Tracked local changes were found. Automatic update was skipped to protect them.
    echo Building the local version.
    exit /b 0
)
git -C "%REPO_DIR%" diff --cached --quiet --ignore-submodules --
if errorlevel 1 (
    echo Staged local changes were found. Automatic update was skipped to protect them.
    echo Building the local version.
    exit /b 0
)

rem This block is parsed before Git can replace the running batch file.
(
    git -C "%REPO_DIR%" merge --ff-only origin/main
    if errorlevel 1 (
        echo The update failed. Building the unchanged local version.
        exit /b 0
    )
    echo Update completed. Restarting the build from the updated files...
    call "%~f0" --skip-update-check
    if errorlevel 1 exit /b 101
    exit /b 100
)

:cancelled
echo.
echo Installation cancelled. The application was not built.
goto :failed

:failed
echo.
cd /d "%TEMP%"
if not defined SDR_BUILD_NO_PAUSE pause
exit /b 1
