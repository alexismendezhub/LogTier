# LogTier: builds, tests and packs the NuGet packages (Windows PowerShell 5+ / pwsh).
# Output: artifacts/ and nuget/ (the nuget folder doubles as a local NuGet feed).
# Usage (from the repo root):  powershell -ExecutionPolicy Bypass -File scripts\pack.ps1
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

# Native commands do not throw on failure, so every dotnet call is checked explicitly.
function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE." }
}

if (Test-Path artifacts) { Remove-Item -Recurse -Force artifacts }
New-Item -ItemType Directory -Force -Path artifacts | Out-Null

Invoke-DotNet restore LogTier.slnx
Invoke-DotNet build LogTier.slnx -c Release --no-restore
Invoke-DotNet test LogTier.slnx -c Release --no-build
Invoke-DotNet pack src/LogTier.Serilog/LogTier.Serilog.csproj -c Release --no-build -o artifacts
Invoke-DotNet pack src/LogTier.Log4Net/LogTier.Log4Net.csproj -c Release --no-build -o artifacts

New-Item -ItemType Directory -Force -Path nuget | Out-Null
Remove-Item nuget/*.nupkg, nuget/*.snupkg -ErrorAction SilentlyContinue
Copy-Item artifacts/*.nupkg, artifacts/*.snupkg -Destination nuget
Write-Host 'LogTier packages built successfully.'
