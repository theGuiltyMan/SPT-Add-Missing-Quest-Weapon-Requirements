# End-to-end release workflow:
#   1. Resolve version (arg, or default from ModMetadata.cs).
#   2. Cross-check ModMetadata.Version matches requested.
#   3. Verify local master == origin/master.
#   4. Monotonic check vs latest GitHub release.
#   5. Run dotnet test (skippable).
#   6. Build release zip (tools/make-release-zip.ps1).
#   7. Resolve release notes (arg, CHANGELOG.md section, or --generate-notes).
#   8. Squash + push to GitHub `public/main` and tag (tools/release-to-public.ps1).
#   9. Push the annotated tag to forgejo `origin` too.
#  10. Prompt user to confirm zip upload.
#  11. Create GitHub Release for the tag and upload the zip as an asset.
#  12. Print the release URL.
#
# Usage:
#   tools/release.ps1                                   # version from ModMetadata.cs
#   tools/release.ps1 v2.0.5
#   tools/release.ps1 v2.0.5 -NotesFile path\to\notes.md
#   tools/release.ps1 -DryRun                           # walk every step, skip pushes / gh release
#   tools/release.ps1 -SkipTests                        # skip the dotnet test pre-flight
#   tools/release.ps1 -AllowVersionMismatch             # ModMetadata.Version != requested
#   tools/release.ps1 -AllowNonMonotonic                # version <= latest GitHub release
#
# Requires: dotnet, git, gh (authenticated against the public GitHub repo).

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Version,

    [Parameter(Position = 1)]
    [string]$NotesFile,

    [switch]$DryRun,
    [switch]$SkipTests,
    [switch]$AllowVersionMismatch,
    [switch]$AllowNonMonotonic
)

$ErrorActionPreference = "Stop"

function Fail([string]$msg) {
    # Throw rather than `exit 1` so callers inside `try` blocks still run their
    # `finally` cleanup. The script-level `$ErrorActionPreference = "Stop"`
    # turns an uncaught throw into a non-zero exit at the script boundary.
    throw $msg
}

function Read-YesNo([string]$prompt) {
    $answer = Read-Host $prompt
    return ($answer -imatch '^y')
}

function Get-MetadataVersion([string]$repoRoot) {
    $metadataFile = Join-Path $repoRoot "AddMissingQuestRequirements\Spt\ModMetadata.cs"
    if (-not (Test-Path -LiteralPath $metadataFile)) {
        Fail "ModMetadata.cs not found at $metadataFile"
    }
    $match = Select-String -Path $metadataFile -Pattern 'Version\s*\{\s*get;\s*init;\s*\}\s*=\s*new\("([^"]+)"\)' | Select-Object -First 1
    if (-not $match) {
        Fail "Could not parse Version from $metadataFile"
    }
    return $match.Matches[0].Groups[1].Value
}

function Get-ChangelogSection([string]$repoRoot, [string]$bareVersion) {
    $changelog = Join-Path $repoRoot "CHANGELOG.md"
    if (-not (Test-Path -LiteralPath $changelog)) {
        return $null
    }
    $lines = Get-Content -LiteralPath $changelog -Encoding UTF8
    $startPattern = '^##\s+\[?' + [regex]::Escape($bareVersion) + '\]?(\s|$)'
    $startIdx = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match $startPattern) {
            $startIdx = $i + 1
            break
        }
    }
    if ($startIdx -lt 0) {
        return $null
    }
    $endIdx = $lines.Count
    for ($j = $startIdx; $j -lt $lines.Count; $j++) {
        if ($lines[$j] -match '^##\s') {
            $endIdx = $j
            break
        }
    }
    # Empty-section guard. PowerShell ranges are inclusive on both ends, so
    # `$startIdx..($endIdx - 1)` when $startIdx == $endIdx becomes a REVERSE
    # range (e.g. 5..4) that returns prior content instead of an empty slice.
    if ($startIdx -ge $endIdx) {
        return $null
    }
    $body = ($lines[$startIdx..($endIdx - 1)] -join "`n").Trim()
    if (-not $body) {
        return $null
    }
    return $body
}

function Compare-SemVer([string]$a, [string]$b) {
    # Returns -1 if a < b, 0 if a == b, 1 if a > b. Pre-release tags lexicographic, simple.
    $stripA = $a -replace '^v', ''
    $stripB = $b -replace '^v', ''
    $aParts = $stripA -split '[-+]', 2
    $bParts = $stripB -split '[-+]', 2
    $aCore  = $aParts[0] -split '\.'
    $bCore  = $bParts[0] -split '\.'
    for ($k = 0; $k -lt 3; $k++) {
        $ai = [int]$aCore[$k]
        $bi = [int]$bCore[$k]
        if ($ai -lt $bi) { return -1 }
        if ($ai -gt $bi) { return 1 }
    }
    $aPre = if ($aParts.Count -gt 1) { $aParts[1] } else { '' }
    $bPre = if ($bParts.Count -gt 1) { $bParts[1] } else { '' }
    # Per semver: a release WITHOUT a pre-release tag is GREATER than one with.
    if (-not $aPre -and $bPre)     { return 1 }
    if ($aPre -and -not $bPre)     { return -1 }
    $strCmp = [string]::Compare($aPre, $bPre, [System.StringComparison]::Ordinal)
    if ($strCmp -lt 0) { return -1 }
    if ($strCmp -gt 0) { return 1 }
    return 0
}

# ── Tool prerequisites ─────────────────────────────────────────────────────────
foreach ($tool in @("git", "dotnet", "gh")) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Fail "required tool not on PATH: $tool"
    }
}

$ScriptDir    = $PSScriptRoot
$RepoRoot     = (Resolve-Path (Join-Path $ScriptDir "..")).Path
$MakeZip      = Join-Path $ScriptDir "make-release-zip.ps1"
$ReleaseToPub = Join-Path $ScriptDir "release-to-public.ps1"

# ── Step 0: resolve version ────────────────────────────────────────────────────
$metaVersion = Get-MetadataVersion -repoRoot $RepoRoot
if (-not $Version) {
    $Version = "v$metaVersion"
    Write-Host "==> defaulting -Version to v$metaVersion (from ModMetadata.cs)"
}

if ($Version -notmatch '^v[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.+-]+)?$') {
    Fail "version must look like vMAJOR.MINOR.PATCH (got: $Version)"
}

$BareVersion = $Version.Substring(1)   # strip leading "v"

# ── Step 1: cross-check requested vs ModMetadata ───────────────────────────────
if ($BareVersion -ne $metaVersion) {
    if (-not $AllowVersionMismatch) {
        Fail @"
version mismatch: requested $Version but ModMetadata.Version is $metaVersion.
Bump AddMissingQuestRequirements/Spt/ModMetadata.cs first, or pass -AllowVersionMismatch.
"@
    }
    Write-Warning "ModMetadata.Version ($metaVersion) does not match requested $Version; -AllowVersionMismatch acknowledged"
}

if ($NotesFile -and -not (Test-Path -LiteralPath $NotesFile -PathType Leaf)) {
    Fail "notes file not found: $NotesFile"
}

# ── Step 2: working-tree + branch sync check ──────────────────────────────────
& git -C $RepoRoot diff-index --quiet HEAD --
if ($LASTEXITCODE -ne 0) {
    Fail "working tree dirty; commit or stash first"
}

$currentBranch = (& git -C $RepoRoot symbolic-ref --short HEAD 2>$null)
if ($LASTEXITCODE -ne 0 -or $currentBranch -ne "master") {
    Write-Warning "not on master (HEAD is '$currentBranch'); release-to-public will read origin/master regardless, but verify intent"
}

& git -C $RepoRoot fetch origin master | Out-Null
if ($LASTEXITCODE -ne 0) {
    Fail "git fetch origin master failed (exit $LASTEXITCODE); cannot trust origin/master sync check"
}
$localHead  = (& git -C $RepoRoot rev-parse master).Trim()
$originHead = (& git -C $RepoRoot rev-parse origin/master).Trim()
if ($localHead -ne $originHead) {
    $ahead  = (& git -C $RepoRoot rev-list --count "origin/master..master").Trim()
    $behind = (& git -C $RepoRoot rev-list --count "master..origin/master").Trim()
    Fail @"
local master out of sync with origin/master (ahead $ahead, behind $behind).
Releases must ship from a pushed, up-to-date master. Push or rebase first.
"@
}

# ── Step 3: monotonic check vs latest GitHub release ──────────────────────────
$PublicUrl = (& git -C $RepoRoot remote get-url public 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $PublicUrl) {
    Fail "'public' remote not configured"
}
if ($PublicUrl -notmatch 'github\.com[:/](?<slug>[^/]+/[^/.]+)(\.git)?$') {
    Fail "could not parse owner/repo from public remote: $PublicUrl"
}
$RepoSlug = $Matches.slug

$latestTag = (& gh release list -R $RepoSlug --limit 1 --json tagName -q '.[0].tagName' 2>$null)
if ($LASTEXITCODE -eq 0 -and $latestTag) {
    $latestTag = $latestTag.Trim()
    $cmp = Compare-SemVer -a $Version -b $latestTag
    if ($cmp -le 0) {
        if (-not $AllowNonMonotonic) {
            Fail "requested version $Version is not greater than the latest GitHub release $latestTag. Pass -AllowNonMonotonic to override."
        }
        Write-Warning "requested $Version is <= latest release $latestTag; -AllowNonMonotonic acknowledged"
    }
} else {
    Write-Host "==> no previous GitHub release found (or gh list failed); skipping monotonic check"
}

# ── Step 4: dotnet test pre-flight ────────────────────────────────────────────
if (-not $SkipTests) {
    Write-Host "==> running dotnet test (use -SkipTests to skip)"
    & dotnet test --filter "FullyQualifiedName!~Integration" --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        Fail "dotnet test failed (exit $LASTEXITCODE); release aborted"
    }
}

# ── Step 5: build zip ─────────────────────────────────────────────────────────
$ModFolder = "GuiltyMan-AddMissingQuestRequirements"
$ZipPath   = Join-Path $RepoRoot "AddMissingQuestRequirements\bin\Release\${ModFolder}-v${BareVersion}.zip"

Write-Host ""
Write-Host "==> building release zip"
& $MakeZip -Version $BareVersion | Out-Host
if ($LASTEXITCODE -ne 0) {
    Fail "make-release-zip.ps1 failed (exit $LASTEXITCODE)"
}
if (-not (Test-Path -LiteralPath $ZipPath)) {
    Fail "expected zip not found: $ZipPath"
}

# ── Step 6: resolve release notes ─────────────────────────────────────────────
$resolvedNotesFile = $null
$resolvedNotesTempCreated = $false

if ($NotesFile) {
    $resolvedNotesFile = (Resolve-Path -LiteralPath $NotesFile).Path
    Write-Host "==> using notes from $resolvedNotesFile"
} else {
    $changelogBody = Get-ChangelogSection -repoRoot $RepoRoot -bareVersion $BareVersion
    if ($changelogBody) {
        $resolvedNotesFile = [System.IO.Path]::GetTempFileName()
        # Use [IO.File]::WriteAllText to avoid BOM on Windows PowerShell 5.x.
        [System.IO.File]::WriteAllText($resolvedNotesFile, $changelogBody, [System.Text.UTF8Encoding]::new($false))
        $resolvedNotesTempCreated = $true
        Write-Host "==> using CHANGELOG.md section [$BareVersion] for release notes"
    } else {
        Write-Host "==> no CHANGELOG.md section [$BareVersion]; will fall back to gh --generate-notes"
    }
}

try {
    # ── Step 7: push to public + tag ──────────────────────────────────────────
    Write-Host ""
    Write-Host "==> running release-to-public.ps1"
    $rtpArgs = @($Version)
    if ($resolvedNotesFile) {
        $rtpArgs += @("-NotesFile", $resolvedNotesFile)
    }
    if ($DryRun) {
        $rtpArgs += @("-DryRun")
    }
    & $ReleaseToPub @rtpArgs
    if ($LASTEXITCODE -ne 0) {
        Fail "release-to-public.ps1 aborted; not uploading zip"
    }

    # ── Step 8: mirror tag to forgejo origin ─────────────────────────────────
    if ($DryRun) {
        Write-Host "==> [dry-run] would push tag $Version to origin"
    } else {
        Write-Host ""
        Write-Host "==> mirroring tag $Version to forgejo origin"
        & git -C $RepoRoot push origin $Version
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "git push origin $Version failed; tag is on public but not on origin. Continuing release."
        }
    }

    # ── Step 9: confirm + create GitHub release ──────────────────────────────
    Write-Host ""
    Write-Host "==> tag $Version is now on $RepoSlug; ready to attach zip"
    Write-Host "    asset: $ZipPath"
    $userAborted = $false
    if (-not (Read-YesNo "upload zip to GitHub Release for $Version? [y/N]")) {
        Write-Host "aborted by user; tag pushed but no release asset uploaded"
        $userAborted = $true
    }

    if ($userAborted) {
        # Intentional clean exit (default 0). The outer `finally` cleans up the temp notes file.
        return
    }

    Write-Host ""
    if ($DryRun) {
        Write-Host "==> [dry-run] would run: gh release create $Version $ZipPath -R $RepoSlug --title $Version"
        if ($resolvedNotesFile) {
            Write-Host "    notes from: $resolvedNotesFile"
        } else {
            Write-Host "    notes: --generate-notes"
        }
        Write-Host ""
        Write-Host "==> [dry-run] complete; no changes pushed"
        return
    }

    Write-Host "==> creating GitHub Release + uploading asset"
    $ghArgs = @(
        "release", "create", $Version, $ZipPath,
        "-R", $RepoSlug,
        "--title", $Version
    )
    if ($resolvedNotesFile) {
        $ghArgs += @("--notes-file", $resolvedNotesFile)
    } else {
        $ghArgs += @("--generate-notes")
    }

    & gh @ghArgs
    if ($LASTEXITCODE -ne 0) {
        Fail "gh release create failed (exit $LASTEXITCODE); zip not uploaded"
    }

    # ── Step 10: print release URL ────────────────────────────────────────────
    $releaseUrl = (& gh release view $Version -R $RepoSlug --json url -q '.url' 2>$null)
    if ($LASTEXITCODE -eq 0 -and $releaseUrl) {
        Write-Host ""
        Write-Host "==> released $Version on $RepoSlug"
        Write-Host "    asset: $(Split-Path -Leaf $ZipPath)"
        Write-Host "    url  : $($releaseUrl.Trim())"
    } else {
        Write-Host ""
        Write-Host "==> released $Version on $RepoSlug with asset $(Split-Path -Leaf $ZipPath)"
    }
}
finally {
    if ($resolvedNotesTempCreated -and (Test-Path -LiteralPath $resolvedNotesFile)) {
        Remove-Item -LiteralPath $resolvedNotesFile -Force -ErrorAction SilentlyContinue
    }
}
