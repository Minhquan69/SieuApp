@echo off
setlocal

rem Tao bo cai iVMS x64 co the copy sang may Windows khac de cai dat.
rem Ket qua: thu muc installer\output\iVMS-Setup-*-x64.exe
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Build-Installer.ps1" -OutputDirectory "%~dp0installer\output"
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo.
    echo Dong goi that bai. Xem loi o phia tren.
    pause
    exit /b %EXIT_CODE%
)

echo.
echo Dong goi thanh cong. Mo thu muc chua bo cai...
explorer.exe "%~dp0installer\output"
endlocal
