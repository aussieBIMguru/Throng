@echo off
rem ---------------------------------------------------------------------------
rem  clean.bat - removes build output so the project can be zipped and handed back clean.
rem
rem  Deletes every bin, obj and TestResults folder under this folder.
rem  Keeps .vs (Visual Studio layout and startup-project settings) and everything else.
rem
rem  Close Visual Studio first so no files are locked.
rem  Usage:  clean.bat        (pauses at the end)
rem          clean.bat /q     (no pause, for scripts)
rem ---------------------------------------------------------------------------
setlocal
cd /d "%~dp0"

echo Cleaning bin, obj and TestResults under:
echo   %CD%
echo.

set /a removed=0
for /d /r . %%D in (bin obj TestResults) do (
    if exist "%%D\" (
        echo   removing %%D
        rd /s /q "%%D" 2>nul
        if exist "%%D\" (
            echo   ! could not remove %%D ^(is Visual Studio still open?^)
        ) else (
            set /a removed+=1
        )
    )
)

echo.
echo Done. Removed %removed% folder(s).
if /i not "%~1"=="/q" pause
endlocal
