@echo off
rem ---------------------------------------------------------------------------
rem  publish.bat - builds a Throng release (round 026, D-415): tests, the self-contained app and
rem  artifacts\Throng-Setup-<version>.exe. Needs the .NET 10 SDK and Inno Setup 6.
rem
rem  Passes any arguments on to build\publish.ps1, for example:
rem    publish.bat -SkipTests      (no tests)
rem    publish.bat -AllTests       (include the long "Slow" tests)
rem    publish.bat -InnoCompiler "D:\Tools\Inno Setup 6\ISCC.exe"
rem    publish.bat -Sign -CertThumbprint <thumbprint>
rem ---------------------------------------------------------------------------
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build\publish.ps1" %*
if errorlevel 1 (
    echo.
    echo Publish FAILED - see the messages above.
)
pause
