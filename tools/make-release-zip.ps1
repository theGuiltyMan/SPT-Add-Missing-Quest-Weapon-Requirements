# Build the mod in Release and pack a release zip with the SPT install layout.
#
# Usage:
#   tools/make-release-zip.ps1                    # uses Version from ModMetadata.cs
#   tools/make-release-zip.ps1 -Version 1.2.3
#   tools/make-release-zip.ps1 -SkipBuild         # use existing build output
#   tools/make-release-zip.ps1 -OutDir K:\drop    # override output directory
#
# Output: {OutDir}\GuiltyMan-AddMissingQuestRequirements-v{Version}.zip
# Layout inside zip:
#   SPT/user/mods/GuiltyMan-AddMissingQuestRequirements/
#     AddMissingQuestRequirements.dll
#     config/config.jsonc
#     MissingQuestWeapons/{Quest,Weapon,Attachment}Overrides.jsonc

[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutDir,
    [switch]$SkipBuild,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$RepoRoot      = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ModProject    = Join-Path $RepoRoot "AddMissingQuestRequirements\AddMissingQuestRequirements.csproj"
$MetadataFile  = Join-Path $RepoRoot "AddMissingQuestRequirements\Spt\ModMetadata.cs"
$BuildOutDir   = Join-Path $RepoRoot "AddMissingQuestRequirements\bin\$Configuration\AddMissingQuestRequirements"
$ModFolderName = "GuiltyMan-AddMissingQuestRequirements"

if (-not $Version) {
    if (-not (Test-Path -LiteralPath $MetadataFile)) {
        throw "ModMetadata.cs not found at $MetadataFile; pass -Version explicitly."
    }
    $match = Select-String -Path $MetadataFile -Pattern 'Version\s*\{\s*get;\s*init;\s*\}\s*=\s*new\("([^"]+)"\)' | Select-Object -First 1
    if (-not $match) {
        throw "Could not parse Version from $MetadataFile; pass -Version explicitly."
    }
    $Version = $match.Matches[0].Groups[1].Value
    Write-Host "==> parsed version $Version from ModMetadata.cs"
}

if ($Version -notmatch '^\d+\.\d+\.\d+(-[A-Za-z0-9.+-]+)?$') {
    throw "Version '$Version' is not a valid semver (MAJOR.MINOR.PATCH[-prerelease])."
}

if (-not $OutDir) {
    $OutDir = Join-Path $RepoRoot "AddMissingQuestRequirements\bin\$Configuration"
}

if (-not $SkipBuild) {
    Write-Host "==> dotnet build -c $Configuration"
    & dotnet build $ModProject -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed (exit $LASTEXITCODE)."
    }
}

$Dll       = Join-Path $BuildOutDir "AddMissingQuestRequirements.dll"
$ConfigSrc = Join-Path $BuildOutDir "config\config.jsonc"
$MqwSrc    = Join-Path $BuildOutDir "MissingQuestWeapons"

foreach ($p in @($Dll, $ConfigSrc, $MqwSrc)) {
    if (-not (Test-Path -LiteralPath $p)) {
        throw "Missing build artefact: $p (run without -SkipBuild)."
    }
}

$StageRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("amqr-release-" + [Guid]::NewGuid().ToString("N"))
$ModStage  = Join-Path $StageRoot "SPT\user\mods\$ModFolderName"

try {
    New-Item -ItemType Directory -Force -Path $ModStage | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $ModStage "config") | Out-Null

    Copy-Item -Path $Dll        -Destination $ModStage -Force
    Copy-Item -Path $ConfigSrc  -Destination (Join-Path $ModStage "config\config.jsonc") -Force
    Copy-Item -Path $MqwSrc     -Destination $ModStage -Recurse -Force

    $ZipName = "$ModFolderName-v$Version.zip"
    $ZipPath = Join-Path $OutDir $ZipName
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    if (Test-Path -LiteralPath $ZipPath) {
        Remove-Item -LiteralPath $ZipPath -Force
    }

    Write-Host "==> compressing → $ZipPath"
    Compress-Archive -Path (Join-Path $StageRoot "SPT") -DestinationPath $ZipPath -CompressionLevel Optimal

    $sizeKB = [math]::Round((Get-Item -LiteralPath $ZipPath).Length / 1KB, 1)
    Write-Host ""
    Write-Host "==> release zip ready"
    Write-Host "    path : $ZipPath"
    Write-Host "    size : $sizeKB KB"
    Write-Host "    ver  : $Version"

    # Emit final path on the pipeline for callers (release.ps1).
    $ZipPath
}
finally {
    if (Test-Path -LiteralPath $StageRoot) {
        Remove-Item -Recurse -Force $StageRoot
    }
}
