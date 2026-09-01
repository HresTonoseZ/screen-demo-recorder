@echo off
setlocal

pushd "%~dp0"

for %%I in ("%~dp0..\..\..") do set "REPOSITORY_USER_HOME=%%~fI"
set "DOTNET_EXE=%REPOSITORY_USER_HOME%\AppData\Local\Microsoft\dotnet\dotnet.exe"
set "DOTNET_CLI_HOME=%REPOSITORY_USER_HOME%"
set "NUGET_PACKAGES=%REPOSITORY_USER_HOME%\.nuget\packages"
set "NUGET_HTTP_CACHE_PATH=%REPOSITORY_USER_HOME%\AppData\Local\NuGet\v3-cache"

if not exist "%DOTNET_EXE%" (
    echo Required .NET SDK launcher was not found:
    echo %DOTNET_EXE%
    echo Install .NET SDK 10.0.400 for the repository owner.
    echo.
    pause
    popd
    exit /b 1
)

echo Using .NET SDK: %DOTNET_EXE%
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-native.ps1" -DotNet "%DOTNET_EXE%" %*
set "BUILD_EXIT=%ERRORLEVEL%"

echo.
if not "%BUILD_EXIT%"=="0" (
    echo Build failed with exit code %BUILD_EXIT%.
) else (
    echo Build completed successfully.
    echo Executable: %~dp0dist\native-preview\ScreenDemoRecorder.exe
)
echo.
pause

popd
exit /b %BUILD_EXIT%
