@echo off
REM ============================================================
REM  LogTier - builds and runs ALL tests on Windows
REM  Requirements: .NET SDK 10 (with the .NET 6 and 8 runtimes for the multi-target package), .NET Framework 4.8
REM  Usage: scripts\build-and-test.cmd   (from the repo root)
REM ============================================================
setlocal
cd /d "%~dp0.."

echo.
echo === 1/2 Restore and build the solution (.slnx), warnings as errors ===
dotnet build LogTier.slnx -c Release || goto :error

echo.
echo === 2/2 .NET tests (xUnit) ===
dotnet test LogTier.slnx -c Release --no-build || goto :error

echo.
echo ===== ALL OK =====
exit /b 0

:error
echo.
echo ===== FAILED =====
exit /b 1
