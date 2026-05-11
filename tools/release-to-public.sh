#!/usr/bin/env bash
# Snapshot forgejo `origin/master` as one release commit on `public/main` and push.
# Usage: tools/release-to-public.sh <version> [notes-file]
#   <version>     e.g. v0.2.0
#   [notes-file]  optional path to a markdown file used as the commit body
#
# Pattern: see docs/RELEASE.md (Pattern B — public = squash-release branch).
# Public and forgejo histories are disjoint by design; never reverse-merge.
#
# Mechanism: `git read-tree --reset -u origin/master` makes the index + working
# tree exactly equal to origin/master; the new commit's parent stays = public/main.
# Result: linear public history, one commit per release, tree byte-identical to
# the released forgejo state. `merge --squash` does NOT work here — disjoint
# histories produce add/add conflicts and union-of-trees, leaving stale paths.

set -euo pipefail

VERSION="${1:-}"
NOTES_FILE="${2:-}"

if [[ -z "$VERSION" ]]; then
    echo "usage: $0 <version> [notes-file]" >&2
    exit 1
fi

if [[ ! "$VERSION" =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z0-9.+-]+)?$ ]]; then
    echo "error: version must look like vMAJOR.MINOR.PATCH (got: $VERSION)" >&2
    exit 1
fi

if [[ -n "$NOTES_FILE" && ! -f "$NOTES_FILE" ]]; then
    echo "error: notes file not found: $NOTES_FILE" >&2
    exit 1
fi

if ! git diff-index --quiet HEAD --; then
    echo "error: working tree dirty; commit or stash first" >&2
    exit 1
fi

if ! git remote get-url public >/dev/null 2>&1; then
    echo "error: 'public' remote not configured" >&2
    exit 1
fi

ORIGINAL_HEAD=$(git symbolic-ref --short HEAD 2>/dev/null || git rev-parse HEAD)

cleanup() {
    git checkout -q "$ORIGINAL_HEAD" 2>/dev/null || true
    git branch -D release-stage 2>/dev/null || true
}
trap cleanup EXIT

echo "==> fetching origin + public"
git fetch origin master
git fetch public main

if git rev-parse -q --verify "refs/tags/$VERSION" >/dev/null; then
    echo "error: tag $VERSION already exists locally" >&2
    exit 1
fi
if git ls-remote --tags public "refs/tags/$VERSION" | grep -q .; then
    echo "error: tag $VERSION already exists on public" >&2
    exit 1
fi

echo "==> staging release-stage branch from public/main"
git checkout -B release-stage public/main

echo "==> loading origin/master tree onto release-stage"
git read-tree --reset -u origin/master

if git diff --cached --quiet; then
    echo "error: no changes to release (public already matches origin/master content)" >&2
    exit 1
fi

# Sanity: staged tree must exactly equal origin/master. If not, abort —
# something stale slipped in (working-tree edits, partial read-tree, etc).
if ! git diff --quiet --cached origin/master; then
    echo "error: staged tree does not match origin/master after read-tree; aborting" >&2
    git diff --cached --stat origin/master | tail -5 >&2
    exit 1
fi

COMMIT_MSG_FILE=$(mktemp)
trap 'rm -f "$COMMIT_MSG_FILE"; cleanup' EXIT
{
    echo "release $VERSION"
    echo
    if [[ -n "$NOTES_FILE" ]]; then
        cat "$NOTES_FILE"
    else
        echo "Squashed from forgejo origin/master @ $(git rev-parse --short origin/master)"
    fi
} > "$COMMIT_MSG_FILE"

git commit -F "$COMMIT_MSG_FILE"
git tag -a "$VERSION" -m "$VERSION"

echo
echo "==> ready to push:"
echo "    public/main  <-  $(git rev-parse --short HEAD)  ($VERSION)"
echo
read -r -p "push to public? [y/N] " confirm
if [[ "$confirm" != "y" && "$confirm" != "Y" ]]; then
    echo "aborted; release-stage branch + tag preserved locally for inspection"
    trap - EXIT
    rm -f "$COMMIT_MSG_FILE"
    exit 1
fi

git push public release-stage:main "$VERSION"

echo "==> released $VERSION to public"
