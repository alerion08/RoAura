# Isolated portable build: no installer and no public release.
$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "build-release.ps1") -Development
if ($LASTEXITCODE -ne 0) { throw "Development build failed." }
