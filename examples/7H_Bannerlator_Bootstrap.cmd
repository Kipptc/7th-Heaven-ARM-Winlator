@echo off
setlocal EnableExtensions EnableDelayedExpansion
title 7th Heaven - Bannerlator Prefix Bootstrap

REM ================================================================
REM  7th Heaven / Bannerlator x86_64 Prefix Bootstrap
REM
REM  Known-good target used while troubleshooting:
REM    - x86_64 Bannerlator container
REM    - Box64 Hybrid Bionic
REM    - Proton 11.0-7.1 x86_64
REM
REM  Put any optional installers/files BESIDE this script.
REM  Then run this script from inside the Wine/Bannerlator container.
REM ================================================================

set "HERE=%~dp0"
set "SEVEN_DIR=%LOCALAPPDATA%\Programs\7th Heaven"

echo.
echo ================================================================
echo   7th Heaven - Bannerlator Prefix Bootstrap
echo ================================================================
echo Script folder:
echo   %HERE%
echo.

REM ----------------------------------------------------------------
REM 1) .NET 9 Desktop Runtime x64
REM    Recognizes normal Microsoft filename:
REM      windowsdesktop-runtime-9.x.x-win-x64.exe
REM ----------------------------------------------------------------
set "FOUND_DOTNET=0"
for %%F in ("%HERE%windowsdesktop-runtime-9*.exe") do (
    if exist "%%~fF" (
        if "!FOUND_DOTNET!"=="0" (
            echo [INSTALL] .NET 9 Desktop Runtime:
            echo           %%~nxF
            start /wait "" "%%~fF" /install /quiet /norestart
            if errorlevel 1 (
                echo [WARN] .NET installer returned errorlevel !errorlevel!.
            ) else (
                echo [ OK ] .NET installer completed.
            )
            set "FOUND_DOTNET=1"
        )
    )
)
if "%FOUND_DOTNET%"=="0" echo [SKIP] No windowsdesktop-runtime-9*.exe found.

REM ----------------------------------------------------------------
REM 2) Visual C++ 2015-2022 Redistributables
REM ----------------------------------------------------------------
if exist "%HERE%VC_redist.x64.exe" (
    echo [INSTALL] VC++ Redistributable x64
    start /wait "" "%HERE%VC_redist.x64.exe" /install /quiet /norestart
    if errorlevel 1 (echo [WARN] VC++ x64 returned errorlevel !errorlevel!.) else (echo [ OK ] VC++ x64 completed.)
) else (
    echo [SKIP] VC_redist.x64.exe not found.
)

if exist "%HERE%VC_redist.x86.exe" (
    echo [INSTALL] VC++ Redistributable x86
    start /wait "" "%HERE%VC_redist.x86.exe" /install /quiet /norestart
    if errorlevel 1 (echo [WARN] VC++ x86 returned errorlevel !errorlevel!.) else (echo [ OK ] VC++ x86 completed.)
) else (
    echo [SKIP] VC_redist.x86.exe not found.
)

REM ----------------------------------------------------------------
REM 3) Microsoft Core Fonts
REM    Old Microsoft web-font installers. Each is optional.
REM ----------------------------------------------------------------
echo.
echo [STEP] Checking for Microsoft Core Font installers...
for %%F in (
    andale32.exe
    arial32.exe
    arialb32.exe
    comic32.exe
    courie32.exe
    georgi32.exe
    impact32.exe
    times32.exe
    trebuc32.exe
    verdan32.exe
    webdin32.exe
) do (
    if exist "%HERE%%%F" (
        echo [INSTALL] %%F
        start /wait "" "%HERE%%%F" /Q:A /R:N
    )
)

REM ----------------------------------------------------------------
REM 4) Real Microsoft Tahoma
REM    THIS WAS IMPORTANT:
REM    WPF was otherwise resolving Wine's bundled Tahoma through a
REM    raw Android/Unix path, which .NET WPF rejected as an invalid URI.
REM
REM    Put these beside this script:
REM      tahoma.ttf
REM      tahomabd.ttf
REM ----------------------------------------------------------------
echo.
if exist "%HERE%tahoma.ttf" (
    echo [COPY] tahoma.ttf -^> C:\Windows\Fonts
    copy /Y "%HERE%tahoma.ttf" "C:\Windows\Fonts\tahoma.ttf" >nul
    reg add "HKLM\Software\Microsoft\Windows NT\CurrentVersion\Fonts" ^
        /v "Tahoma (TrueType)" /t REG_SZ /d "tahoma.ttf" /f >nul
    echo [ OK ] Tahoma Regular installed and registered.
) else (
    echo [WARN] tahoma.ttf not found beside this script.
)

if exist "%HERE%tahomabd.ttf" (
    echo [COPY] tahomabd.ttf -^> C:\Windows\Fonts
    copy /Y "%HERE%tahomabd.ttf" "C:\Windows\Fonts\tahomabd.ttf" >nul
    reg add "HKLM\Software\Microsoft\Windows NT\CurrentVersion\Fonts" ^
        /v "Tahoma Bold (TrueType)" /t REG_SZ /d "tahomabd.ttf" /f >nul
    echo [ OK ] Tahoma Bold installed and registered.
) else (
    echo [WARN] tahomabd.ttf not found beside this script.
)

REM ----------------------------------------------------------------
REM 5) .NET GC virtual-address-space workaround for Box64/Wine
REM    0x80000000 = 2 GiB GC heap hard limit.
REM    Set it for this process AND persist it for future Wine processes.
REM ----------------------------------------------------------------
echo.
echo [CONFIG] Setting DOTNET_GCHeapHardLimit=80000000
set "DOTNET_GCHeapHardLimit=80000000"
reg add "HKCU\Environment" ^
    /v "DOTNET_GCHeapHardLimit" /t REG_SZ /d "80000000" /f >nul
echo [ OK ] .NET GC limit configured.

REM ----------------------------------------------------------------
REM 6) Force WPF software rendering.
REM    THIS WAS THE FIX for the final vkCreateGraphicsPipelines crash.
REM ----------------------------------------------------------------
echo [CONFIG] Disabling WPF hardware acceleration
reg add "HKCU\Software\Microsoft\Avalon.Graphics" ^
    /v "DisableHWAcceleration" /t REG_DWORD /d 1 /f >nul
echo [ OK ] WPF hardware acceleration disabled.

REM ----------------------------------------------------------------
REM 7) DirectInput override
REM    Current 7H-on-Wine guidance calls for native dinput.
REM
REM    Optional automation:
REM      dinput_x64.dll  -> C:\Windows\System32\dinput.dll
REM      dinput_x86.dll  -> C:\Windows\SysWOW64\dinput.dll
REM
REM    If these files are absent, the override is still created so a
REM    separately installed native dinput can be used later.
REM ----------------------------------------------------------------
echo.
if exist "%HERE%dinput_x64.dll" (
    echo [COPY] Native x64 dinput.dll
    copy /Y "%HERE%dinput_x64.dll" "C:\Windows\System32\dinput.dll" >nul
)
if exist "%HERE%dinput_x86.dll" (
    echo [COPY] Native x86 dinput.dll
    copy /Y "%HERE%dinput_x86.dll" "C:\Windows\SysWOW64\dinput.dll" >nul
)

reg add "HKCU\Software\Wine\DllOverrides" ^
    /v "dinput" /t REG_SZ /d "native,builtin" /f >nul
echo [ OK ] dinput override set to native,builtin.
echo        NOTE: This does NOT magically install native dinput if the
echo        DLLs/component are not already present.

REM ----------------------------------------------------------------
REM 8) ICU workaround
REM
REM    The ARM64EC/FEX prefix hit:
REM      Cannot get symbol u_charsToUChars from libicuuc / Error 127
REM
REM    Our x86_64/Box64 prefix did NOT require this, so it is deliberately
REM    disabled by default. If a recreated prefix hits that exact error,
REM    remove "REM " from the next two lines and rerun this script.
REM ----------------------------------------------------------------
REM reg add "HKCU\Software\Wine\DllOverrides" /v "icu" /t REG_SZ /d "" /f >nul
REM echo [ OK ] Wine icu.dll disabled.

REM ----------------------------------------------------------------
REM 9) Optional 7th Heaven installer
REM    If a 7th Heaven installer is beside this script, run it now.
REM    This is intentionally interactive so you can see installer errors.
REM ----------------------------------------------------------------
echo.
set "SEVEN_INSTALLER="
for %%F in ("%HERE%7thHeaven*.exe" "%HERE%7ThHeaven*.exe") do (
    if exist "%%~fF" (
        if not defined SEVEN_INSTALLER set "SEVEN_INSTALLER=%%~fF"
    )
)

if defined SEVEN_INSTALLER (
    echo [FOUND] 7th Heaven installer:
    echo         !SEVEN_INSTALLER!
    choice /C YN /N /M "Run the 7th Heaven installer now? [Y/N] "
    if errorlevel 2 (
        echo [SKIP] 7th Heaven installer not launched.
    ) else (
        start /wait "" "!SEVEN_INSTALLER!"
    )
) else (
    echo [SKIP] No 7thHeaven*.exe installer found beside this script.
)

REM ----------------------------------------------------------------
REM 10) 7H DXVK compatibility config
REM     Upstream Wine guidance recommends shaderModel=1 for black menus.
REM ----------------------------------------------------------------
echo.
if exist "%SEVEN_DIR%\7th Heaven.exe" (
    echo [CONFIG] Writing "%SEVEN_DIR%\dxvk.conf"
    >"%SEVEN_DIR%\dxvk.conf" echo d3d9.shaderModel = 1
    echo [ OK ] dxvk.conf created.
) else (
    echo [WARN] 7th Heaven is not installed at:
    echo        %SEVEN_DIR%
    echo        Run this script again after installing 7th Heaven so
    echo        dxvk.conf can be created.
)

REM ----------------------------------------------------------------
REM 11) Verification summary
REM ----------------------------------------------------------------
echo.
echo ================================================================
echo   Verification
echo ================================================================
echo.

echo -- WPF --
reg query "HKCU\Software\Microsoft\Avalon.Graphics" ^
    /v "DisableHWAcceleration" 2>nul

echo.
echo -- .NET GC --
reg query "HKCU\Environment" ^
    /v "DOTNET_GCHeapHardLimit" 2>nul

echo.
echo -- dinput override --
reg query "HKCU\Software\Wine\DllOverrides" ^
    /v "dinput" 2>nul

echo.
echo -- Tahoma --
if exist "C:\Windows\Fonts\tahoma.ttf" (
    echo FOUND: C:\Windows\Fonts\tahoma.ttf
) else (
    echo MISSING: C:\Windows\Fonts\tahoma.ttf
)
if exist "C:\Windows\Fonts\tahomabd.ttf" (
    echo FOUND: C:\Windows\Fonts\tahomabd.ttf
) else (
    echo MISSING: C:\Windows\Fonts\tahomabd.ttf
)

echo.
echo ================================================================
echo   DONE
echo ================================================================
echo.
echo IMPORTANT:
echo   Fully STOP the Bannerlator container / Wine session and start it
echo   again before launching 7th Heaven. The persisted environment and
echo   WPF settings need to be inherited by a fresh Wine process tree.
echo.
echo Bannerlator-side settings are NOT controllable from this script.
echo Keep the recreated container on the same x86_64 / Box64 / Proton
echo configuration that worked for you.
echo.
pause
endlocal
