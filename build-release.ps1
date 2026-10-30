param(
    # Terminate a running RoAura.exe before publishing. Off by default so a
    # build never kills the developer's live session by accident.
    [switch]$KillRunning,
    [switch]$Development
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $projectRoot

if ($Development -and $KillRunning) { throw "DEV build must not stop stable RoAura." }
if ($KillRunning) {
    Stop-Process -Name RoAura -Force -ErrorAction SilentlyContinue
}

# The version is defined once in RoAura.csproj; every artifact name derives from it.
[xml]$projectXml = Get-Content -Raw .\RoAura.csproj
$projectVersion = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($projectVersion)) {
    throw "RoAura.csproj does not define <Version>."
}

$artifactRoot = Join-Path $projectRoot "artifacts"
$publishDir = Join-Path $artifactRoot $(if ($Development) { "dev/publish" } else { "publish" })
$revision = if ($env:GITHUB_SHA -and $env:GITHUB_SHA.Length -ge 8) { $env:GITHUB_SHA.Substring(0,8) } else { "local" }
$portableZip = Join-Path $artifactRoot $(if ($Development) { "dev/RoAura-Dev-$projectVersion-$revision-portable.zip" } else { "RoAura-$projectVersion-portable.zip" })
$developmentFlag = if ($Development) { "true" } else { "false" }

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) {
    throw "The .NET SDK was not found. Install the .NET 8 SDK (x64), reopen PowerShell, and run this script again."
}

$installedSdks = & dotnet --list-sdks
if ($LASTEXITCODE -ne 0) {
    throw "Could not query installed .NET SDKs (dotnet --list-sdks failed with exit code $LASTEXITCODE)."
}

if (-not ($installedSdks -match '^8\.')) {
    $installedSdkText = ($installedSdks | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($installedSdkText)) {
        $installedSdkText = "none"
    }

    throw "The .NET 8 SDK (x64) is required. Installed SDKs: $installedSdkText. Download: https://dotnet.microsoft.com/download/dotnet/8.0"
}

# app.manifest cannot read the csproj version; fail early if it drifted so the
# executable's embedded manifest and file version never disagree.
$manifestText = Get-Content -Raw .\app.manifest
$manifestVersionMatch = [regex]::Match($manifestText, '<assemblyIdentity\s+version="([^"]+)"')
if (-not $manifestVersionMatch.Success -or $manifestVersionMatch.Groups[1].Value -ne "$projectVersion.0") {
    throw "Version mismatch: RoAura.csproj=$projectVersion, app.manifest=$($manifestVersionMatch.Groups[1].Value). Expected '$projectVersion.0'."
}

# RU and EN dictionaries must expose exactly the same keys. Also verify every
# DynamicResource used by a WPF view against both dictionaries so a missing
# translation fails before a user sees an empty label at runtime.
$resourceKeyPattern = 'x:Key="([^"]+)"'
$dynamicKeyPattern = 'DynamicResource\s+([^\s\}]+)'
$ruText = Get-Content -Raw .\Resources\Strings.ru.xaml
$enText = Get-Content -Raw .\Resources\Strings.en.xaml
$ruKeys = @([regex]::Matches($ruText, $resourceKeyPattern) | ForEach-Object { $_.Groups[1].Value })
$enKeys = @([regex]::Matches($enText, $resourceKeyPattern) | ForEach-Object { $_.Groups[1].Value })
$onlyRu = @($ruKeys | Where-Object { $_ -notin $enKeys })
$onlyEn = @($enKeys | Where-Object { $_ -notin $ruKeys })
if ($onlyRu.Count -gt 0 -or $onlyEn.Count -gt 0) {
    throw "Localization dictionaries differ. RU-only: $($onlyRu -join ', '); EN-only: $($onlyEn -join ', ')."
}
$dynamicKeys = @(Get-ChildItem . -Filter *.xaml -File | ForEach-Object {
    [regex]::Matches((Get-Content -Raw $_.FullName), $dynamicKeyPattern) | ForEach-Object { $_.Groups[1].Value }
} | Sort-Object -Unique)
$missingRu = @($dynamicKeys | Where-Object { $_ -notin $ruKeys })
$missingEn = @($dynamicKeys | Where-Object { $_ -notin $enKeys })
if ($missingRu.Count -gt 0 -or $missingEn.Count -gt 0) {
    throw "Missing localized DynamicResource. RU: $($missingRu -join ', '); EN: $($missingEn -join ', ')."
}
Write-Host "Version and RU/EN resource validation passed."

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

& dotnet publish .\RoAura.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:RoAuraDevelopment=$developmentFlag `
  -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE. Release files were not created."
}

$publishedExecutable = Join-Path $publishDir "RoAura.exe"
if (-not (Test-Path $publishedExecutable -PathType Leaf)) {
    throw "Publish completed without the expected executable: $publishedExecutable"
}

# Launch the published executable in a deterministic startup-only mode. The
# process constructs the real WPF windows and exits by itself, so XAML/runtime
# initialization errors fail the release before an archive is created.
Write-Host "Running RoAura startup smoke test..."
$previousSmokeData = $env:ROAURA_SMOKE_DATA
$smokeDataRoot = Join-Path ([IO.Path]::GetTempPath()) ("RoAura-Smoke-" + [guid]::NewGuid())
$env:ROAURA_SMOKE_DATA = $smokeDataRoot
try {
$smokeProcess = Start-Process -FilePath $publishedExecutable -ArgumentList "--smoke-test" -PassThru
$startupSmokeTimeout = if ($env:ROAURA_QUEST_UI_SNAPSHOTS) { 45000 } else { 15000 }
if (-not $smokeProcess.WaitForExit($startupSmokeTimeout)) {
    Stop-Process -Id $smokeProcess.Id -Force -ErrorAction SilentlyContinue
    throw "RoAura startup smoke test timed out. Release files were not created."
}
if ($smokeProcess.ExitCode -ne 0) {
    $smokeLog = Join-Path $smokeDataRoot "Roaming\debug.log"
    if (Test-Path $smokeLog -PathType Leaf) {
        Write-Host "----- RoAura smoke-test debug.log -----"
        Get-Content $smokeLog -Tail 120
        Write-Host "----- end debug.log -----"
    }
    throw "RoAura startup smoke test failed with exit code $($smokeProcess.ExitCode). See the isolated smoke log above. Release files were not created."
}
} finally {
    $env:ROAURA_SMOKE_DATA = $previousSmokeData
    Remove-Item $smokeDataRoot -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host "Startup smoke test passed."

Copy-Item .\README.md (Join-Path $publishDir "README.md") -Force
Copy-Item .\GUIDE.md (Join-Path $publishDir "GUIDE.md") -Force
Copy-Item .\GUIDE_RU.md (Join-Path $publishDir "GUIDE_RU.md") -Force
Copy-Item .\GUIDE_EN.md (Join-Path $publishDir "GUIDE_EN.md") -Force
Copy-Item .\RELEASE_NOTES.md (Join-Path $publishDir "RELEASE_NOTES.md") -Force

# A public build must never contain the developer's working configuration,
# presets, recovery copies, logs or cached market responses. These files are
# normally stored in %APPDATA%, but the explicit purge also protects releases
# if somebody accidentally places a copy in the project/publish directory.
$privateNames = @(
    "settings.json",
    "active-profile.txt",
    "springro-market-cache.json",
    "springro-market-snapshot.json",
    "market-watch-history.json",
    "debug.log"
)
foreach ($privateName in $privateNames) {
    Get-ChildItem -Path $publishDir -Recurse -Force -File -Filter $privateName -ErrorAction SilentlyContinue |
        Remove-Item -Force
}
foreach ($privateDirectory in @("Profiles", "Recovery")) {
    Get-ChildItem -Path $publishDir -Recurse -Force -Directory -Filter $privateDirectory -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force
}

if (Test-Path $portableZip) {
    Remove-Item $portableZip -Force
}
# ZipFile.CreateFromDirectory writes forward-slash entry names on every
# PowerShell version; Windows PowerShell 5.1 Compress-Archive emits backslash
# paths that some extractors turn into flat files named "Data\x.json".
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($publishDir, $portableZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

if (-not (Test-Path $portableZip -PathType Leaf)) {
    throw "Could not create the portable archive: $portableZip"
}

$archive = [System.IO.Compression.ZipFile]::OpenRead($portableZip)
try {
    $privateEntry = $archive.Entries | Where-Object {
        $leaf = [System.IO.Path]::GetFileName($_.FullName)
        $segments = $_.FullName -split '[/\\]'
        $privateNames -contains $leaf -or
        $segments -contains "Profiles" -or
        $segments -contains "Recovery"
    } | Select-Object -First 1
    if ($null -ne $privateEntry) {
        throw "Release validation failed: private file found in archive: $($privateEntry.FullName)"
    }
}
finally {
    $archive.Dispose()
}

$portableHash = (Get-FileHash $portableZip -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Portable build: $portableZip"
Write-Host "SHA256:         $portableHash"
Write-Host "Executable:     $publishedExecutable"
