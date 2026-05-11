# End-to-end release workflow:
#   1. Build release zip (tools/make-release-zip.ps1).
#   2. Squash + push to GitHub `public/main` and tag (tools/release-to-public.ps1).
#   3. Prompt user to confirm zip upload.
#   4. Create GitHub Release for the tag and upload the zip as an asset.
#
# Usage: tools/release.ps1 <version> [-NotesFile <path>]
#   <version>     e.g. v1.0.3 (must match vMAJOR.MINOR.PATCH; bare zip filename
#                 drops the leading "v")
#   -NotesFile    optional markdown file used for both the squash commit body
#                 and the GitHub Release body.
#
# Requires: dotnet, git, gh (authenticated against the public GitHub repo).

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version,

    [Parameter(Position = 1)]
    [string]$NotesFile
)

$ErrorActionPreference = "Stop"

function Fail([string]$msg) {
    Write-Error $msg
    exit 1
}

if ($Version -notmatch '^v[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.+-]+)?$') {
    Fail "version must look like vMAJOR.MINOR.PATCH (got: $Version)"
}

if ($NotesFile -and -not (Test-Path -LiteralPath $NotesFile -PathType Leaf)) {
    Fail "notes file not found: $NotesFile"
}

foreach ($tool in @("git", "dotnet", "gh")) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Fail "required tool not on PATH: $tool"
    }
}

$ScriptDir     = $PSScriptRoot
$RepoRoot      = (Resolve-Path (Join-Path $ScriptDir "..")).Path
$BareVersion   = $Version.Substring(1)                       # strip leading "v"
$ModFolder     = "GuiltyMan-AddMissingQuestRequirements"
$ZipPath       = Join-Path $RepoRoot "AddMissingQuestRequirements\bin\Release\${ModFolder}-v${BareVersion}.zip"
$MakeZip       = Join-Path $ScriptDir "make-release-zip.ps1"
$ReleaseToPub  = Join-Path $ScriptDir "release-to-public.ps1"

# Resolve public remote slug for `gh -R owner/repo`.
$PublicUrl = (& git -C $RepoRoot remote get-url public 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $PublicUrl) {
    Fail "'public' remote not configured"
}
if ($PublicUrl -notmatch 'github\.com[:/](?<slug>[^/]+/[^/.]+)(\.git)?$') {
    Fail "could not parse owner/repo from public remote: $PublicUrl"
}
$RepoSlug = $Matches.slug

Write-Host "==> [1/4] building release zip"
& $MakeZip -Version $BareVersion | Out-Host
if ($LASTEXITCODE -ne 0) {
    Fail "make-release-zip.ps1 failed (exit $LASTEXITCODE)"
}
if (-not (Test-Path -LiteralPath $ZipPath)) {
    Fail "expected zip not found: $ZipPath"
}

Write-Host ""
Write-Host "==> [2/4] running release-to-public.ps1"
$rtpArgs = @($Version)
if ($NotesFile) {
    $rtpArgs += $NotesFile
}
& $ReleaseToPub @rtpArgs
if ($LASTEXITCODE -ne 0) {
    Fail "release-to-public.ps1 aborted; not uploading zip"
}

Write-Host ""
Write-Host "==> [3/4] tag $Version is now on $RepoSlug; ready to attach zip"
Write-Host "    asset: $ZipPath"
$confirm = Read-Host "upload zip to GitHub Release for $Version? [y/N]"
if ($confirm -ne "y" -and $confirm -ne "Y") {
    Write-Host "aborted; tag pushed but no release asset uploaded"
    exit 1
}

Write-Host ""
Write-Host "==> [4/4] creating GitHub Release + uploading asset"
$ghArgs = @(
    "release", "create", $Version, $ZipPath,
    "-R", $RepoSlug,
    "--title", "$Version"
)
if ($NotesFile) {
    $ghArgs += @("--notes-file", $NotesFile)
} else {
    $ghArgs += @("--generate-notes")
}

& gh @ghArgs
if ($LASTEXITCODE -ne 0) {
    Fail "gh release create failed (exit $LASTEXITCODE); zip not uploaded"
}

Write-Host ""
Write-Host "==> released $Version on $RepoSlug with asset $(Split-Path -Leaf $ZipPath)"
