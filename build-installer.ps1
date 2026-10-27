$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $projectRoot

# build-release.ps1 publishes to artifacts\publish and copies README/GUIDE/
# RELEASE_NOTES into it, so installer.iss can source every payload from there.
& .\build-release.ps1

# The version is defined once in RoAura.csproj and forwarded to Inno Setup.
[xml]$projectXml = Get-Content -Raw .\RoAura.csproj
$projectVersion = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($projectVersion)) {
    throw "RoAura.csproj does not define <Version>."
}

$compilerCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$compiler = $compilerCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $compiler) {
    throw "Inno Setup 6 not found. Install it from https://jrsoftware.org/isdl.php"
}

& $compiler "/DMyAppVersion=$projectVersion" .\installer.iss
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE. The installer was not created."
}

$setupExe = Join-Path $projectRoot "artifacts\RoAura-$projectVersion-Setup.exe"
if (-not (Test-Path $setupExe -PathType Leaf)) {
    throw "ISCC completed without the expected installer: $setupExe"
}

Write-Host "Installer created in the artifacts folder."
foreach ($output in @($setupExe, (Join-Path $projectRoot "artifacts\RoAura-$projectVersion-portable.zip"))) {
    if (Test-Path $output -PathType Leaf) {
        $hash = (Get-FileHash $output -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-Host "$hash  $([System.IO.Path]::GetFileName($output))"
    }
}
