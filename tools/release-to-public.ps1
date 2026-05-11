# Snapshot forgejo `origin/master` as one release commit on `public/main` and push.
# Usage: tools/release-to-public.ps1 <version> [-NotesFile <path>]
#   <version>     e.g. v0.2.0
#   -NotesFile    optional path to a markdown file used as the commit body
#
# Pattern: see docs/RELEASE.md (Pattern B — public = squash-release branch).
# Public and forgejo histories are disjoint by design; never reverse-merge.
#
# Mechanism: `git read-tree --reset -u origin/master` makes the index + working
# tree exactly equal to origin/master; the new commit's parent stays = public/main.
# Result: linear public history, one commit per release, tree byte-identical to
# the released forgejo state. `merge --squash` does NOT work here — disjoint
# histories produce add/add conflicts and union-of-trees, leaving stale paths.

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

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$GitArgs)
    & git @GitArgs
    if ($LASTEXITCODE -ne 0) {
        Fail "git $($GitArgs -join ' ') failed (exit $LASTEXITCODE)"
    }
}

if ($Version -notmatch '^v[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.+-]+)?$') {
    Fail "version must look like vMAJOR.MINOR.PATCH (got: $Version)"
}

if ($NotesFile -and -not (Test-Path -LiteralPath $NotesFile -PathType Leaf)) {
    Fail "notes file not found: $NotesFile"
}

& git diff-index --quiet HEAD --
if ($LASTEXITCODE -ne 0) {
    Fail "working tree dirty; commit or stash first"
}

& git remote get-url public *> $null
if ($LASTEXITCODE -ne 0) {
    Fail "'public' remote not configured"
}

$OriginalHead = (& git symbolic-ref --short HEAD 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $OriginalHead) {
    $OriginalHead = (& git rev-parse HEAD).Trim()
}

$CommitMsgFile = $null
$ReleaseStageCreated = $false

$cleanup = {
    if ($OriginalHead) {
        & git checkout -q $OriginalHead *> $null
    }
    if ($ReleaseStageCreated) {
        & git branch -D release-stage *> $null
    }
    if ($CommitMsgFile -and (Test-Path -LiteralPath $CommitMsgFile)) {
        Remove-Item -LiteralPath $CommitMsgFile -Force -ErrorAction SilentlyContinue
    }
}

try {
    Write-Host "==> fetching origin + public"
    Invoke-Git fetch origin master
    Invoke-Git fetch public main

    & git rev-parse -q --verify "refs/tags/$Version" *> $null
    if ($LASTEXITCODE -eq 0) {
        Fail "tag $Version already exists locally"
    }

    $remoteTag = & git ls-remote --tags public "refs/tags/$Version"
    if ($remoteTag) {
        Fail "tag $Version already exists on public"
    }

    Write-Host "==> staging release-stage branch from public/main"
    Invoke-Git checkout -B release-stage public/main
    $ReleaseStageCreated = $true

    Write-Host "==> loading origin/master tree onto release-stage"
    Invoke-Git read-tree --reset -u origin/master

    & git diff --cached --quiet
    if ($LASTEXITCODE -eq 0) {
        Fail "no changes to release (public already matches origin/master content)"
    }

    # Sanity: staged tree must exactly equal origin/master. If not, abort —
    # something stale slipped in (working-tree edits, partial read-tree, etc).
    & git diff --quiet --cached origin/master
    if ($LASTEXITCODE -ne 0) {
        & git diff --cached --stat origin/master | Select-Object -Last 5 | Write-Error
        Fail "staged tree does not match origin/master after read-tree; aborting"
    }

    $CommitMsgFile = [System.IO.Path]::GetTempFileName()
    $lines = @("release $Version", "")
    if ($NotesFile) {
        $lines += (Get-Content -LiteralPath $NotesFile -Raw)
    } else {
        $shortSha = (& git rev-parse --short origin/master).Trim()
        $lines += "Squashed from forgejo origin/master @ $shortSha"
    }
    Set-Content -LiteralPath $CommitMsgFile -Value ($lines -join "`n") -NoNewline -Encoding utf8

    Invoke-Git commit -F $CommitMsgFile
    Invoke-Git tag -a $Version -m $Version

    Write-Host ""
    $headShort = (& git rev-parse --short HEAD).Trim()
    Write-Host "==> ready to push:"
    Write-Host "    public/main  <-  $headShort  ($Version)"
    Write-Host ""
    $confirm = Read-Host "push to public? [y/N]"
    if ($confirm -ne "y" -and $confirm -ne "Y") {
        Write-Host "aborted; release-stage branch + tag preserved locally for inspection"
        # Skip cleanup so the user can inspect release-stage + the new tag.
        $ReleaseStageCreated = $false
        $OriginalHead = $null
        if ($CommitMsgFile -and (Test-Path -LiteralPath $CommitMsgFile)) {
            Remove-Item -LiteralPath $CommitMsgFile -Force -ErrorAction SilentlyContinue
        }
        $CommitMsgFile = $null
        exit 1
    }

    Invoke-Git push public "release-stage:main" $Version

    Write-Host "==> released $Version to public"
}
finally {
    & $cleanup
}
