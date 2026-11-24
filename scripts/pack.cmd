@echo off
REM ============================================================
REM  LogTier - builds, tests and packs the NuGet packages
REM  Output: artifacts\ and nuget\ (the nuget folder doubles as a local NuGet feed)
REM  Usage: scripts\pack.cmd   (from the repo root)
REM ============================================================
setlocal
cd /d "%~dp0.."
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\pack.ps1 || exit /b 1
