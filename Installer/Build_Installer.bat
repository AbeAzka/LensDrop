@echo off
title Build Installer LensDrop Studio v0.2.0 - Indodev Studio
echo ========================================================
echo   1. Membersihkan build lama & Publish Self-Contained
echo ========================================================
cd /d "%~dp0.."
dotnet clean -c Release
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Gagal melakukan dotnet publish!
    pause
    exit /b 1
)

echo.
echo ========================================================
echo   2. Mengompilasi Installer dengan Inno Setup (ISCC)
echo ========================================================
set ISCC_PATH="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if not exist %ISCC_PATH% set ISCC_PATH="C:\Program Files\Inno Setup 7\ISCC.exe"
if exist %ISCC_PATH% (
    %ISCC_PATH% "Installer\LensDrop_Setup.iss"
    echo.
    echo [SUKSES] File installer siap di folder InstallerOutput\
) else (
    echo [INFO] Inno Setup tidak ditemukan di lokasi default.
    echo Hasil publish siap dibungkus di: bin\Release\net8.0-windows\win-x64\publish\
    echo Buka file Installer\LensDrop_Setup.iss menggunakan Inno Setup Compiler secara manual.
)
pause
