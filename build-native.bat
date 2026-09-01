@echo off
setlocal

pushd "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-native.ps1" %*
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
