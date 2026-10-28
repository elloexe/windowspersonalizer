@echo off
REM ============================================================
REM  Build WindowsPersonalizer.exe yourself on Windows.
REM  Requires the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
REM  Double-click this file, or run it from a terminal.
REM  (A ready-made exe is already in the "release" folder.)
REM ============================================================
setlocal

echo [Personalizer] Building WindowsPersonalizer.exe (this takes a minute)...
dotnet publish src\WindowsPersonalizer\WindowsPersonalizer.csproj -c Release -o release || goto :err

echo.
echo [Personalizer] Done. Your executable is here:
echo     %CD%\release\WindowsPersonalizer.exe
echo.
pause
endlocal
exit /b 0

:err
echo.
echo [Personalizer] Build failed. Make sure the .NET 8 SDK is installed
echo                ^(run: dotnet --list-sdks^).
echo.
pause
endlocal
exit /b 1
