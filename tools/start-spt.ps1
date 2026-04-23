# Resolve SptRoot from local.props (lives in the repo root, not alongside this script).
$repoRoot   = Split-Path -Parent $PSScriptRoot
$localProps = Join-Path $repoRoot "local.props"
$sptRoot = $null

if (Test-Path $localProps) {
    $xml = [xml](Get-Content $localProps)
    $sptRoot = $xml.Project.PropertyGroup.SptRoot
}

if (-not $sptRoot -or $sptRoot -eq "CHANGE_ME") {
    Write-Error "SptRoot not set. Copy local.props.template to local.props and fill in SptRoot."
    exit 1
}

$shortcut = Join-Path $sptRoot "SPT.Server.lnk"

if (-not (Test-Path $shortcut)) {
    Write-Error "SPT.Server.lnk not found at: $shortcut"
    exit 1
}

Write-Host "Starting SPT server: $shortcut"
Start-Process -FilePath $shortcut
