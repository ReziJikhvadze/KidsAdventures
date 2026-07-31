<#
.SYNOPSIS
    Installs the repository's git hooks. Run once per clone.

.DESCRIPTION
    Git hooks are not versioned by git itself, so they have to be installed into
    .git/hooks explicitly. This points core.hooksPath at a tracked directory instead,
    which means the hooks travel with the repository and stay in review.

    The pre-commit hook exists because this repository has already leaked credentials
    once: appsettings.Production.json was committed with the Azure SQL password, the
    OpenAI key, the storage account key and a Gmail app password in it. The hook makes
    the next one much harder.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = git rev-parse --show-toplevel
if (-not $repoRoot) { throw "Not inside a git repository." }

$hooksDir = Join-Path $repoRoot ".githooks"
New-Item -ItemType Directory -Force -Path $hooksDir | Out-Null

$preCommit = @'
#!/usr/bin/env bash
# Blocks a commit that would add a credential or a build artifact.
# Bypass with `git commit --no-verify` when you are certain.
set -uo pipefail

staged=$(git diff --cached --name-only --diff-filter=ACM)
[ -z "$staged" ] && exit 0

fail=0

# 1. Files that must never be tracked, whatever they contain.
while IFS= read -r file; do
  case "$file" in
    *appsettings.Production.json|*.env.local|*.pfx|*.pem|*publish.zip|*frontend-deploy.zip)
      echo "BLOCKED: $file must not be committed."
      fail=1
      ;;
  esac
done <<< "$staged"

# 2. Credential shapes, scanned in the staged content rather than the file on disk.
patterns='sk-proj-[A-Za-z0-9_-]{20}|sk-[A-Za-z0-9]{32}|AccountKey=[A-Za-z0-9+/]{40}|xox[baprs]-|-----BEGIN [A-Z ]*PRIVATE KEY-----|AKIA[0-9A-Z]{16}'
while IFS= read -r file; do
  case "$file" in
    *.png|*.jpg|*.jpeg|*.webp|*.woff2|*.ttf|*.zip|*.pdf|*package-lock.json) continue ;;
  esac
  if git show ":$file" 2>/dev/null | grep -nEq "$patterns"; then
    echo "BLOCKED: $file looks like it contains a credential."
    git show ":$file" | grep -nE "$patterns" | head -3 | sed 's/^/    /'
    fail=1
  fi
done <<< "$staged"

if [ "$fail" -ne 0 ]; then
  echo ""
  echo "Nothing was committed. Move the secret to App Service configuration."
  echo "See KidsAdventuresAPI/docs/SECRETS_ROTATION.md"
  echo "If this is a false positive: git commit --no-verify"
  exit 1
fi
exit 0
'@

$prePush = @'
#!/usr/bin/env bash
# Cheap local mirror of CI: typecheck before the push, not after the pipeline fails.
set -uo pipefail
root=$(git rev-parse --show-toplevel)
cd "$root/KidsAdventuresAPI/wwwroot" || exit 0
[ -d node_modules ] || exit 0
echo "pre-push: typechecking frontend..."
if ! npx tsc --noEmit -p tsconfig.json; then
  echo "pre-push: typecheck failed. Push aborted (--no-verify to override)."
  exit 1
fi
exit 0
'@

# LF endings: git runs these through bash even on Windows, and CRLF breaks the shebang.
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $hooksDir "pre-commit"), ($preCommit -replace "`r`n", "`n"), $utf8NoBom)
[System.IO.File]::WriteAllText((Join-Path $hooksDir "pre-push"), ($prePush -replace "`r`n", "`n"), $utf8NoBom)

git -C $repoRoot config core.hooksPath ".githooks"
git -C $repoRoot update-index --chmod=+x .githooks/pre-commit 2>$null
git -C $repoRoot update-index --chmod=+x .githooks/pre-push 2>$null

Write-Host "Hooks installed to .githooks and core.hooksPath set." -ForegroundColor Green
Write-Host "Verify with: git config core.hooksPath"
