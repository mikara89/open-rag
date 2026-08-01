# Lightweight, dependency-free documentation checks shared by local CI and GitHub Actions.
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$requiredFiles = @(
    "README.md",
    "docs/05-processing-pipeline-and-cap.md",
    "docs/10-configuration-and-secrets.md",
    "docs/11-mvp-local-run.md",
    "docs/12-production-readiness-roadmap.md",
    "docs/13-documentation-review-checklist.md",
    "docs/14-github-governance.md",
    "docs/15-authentication.md",
    "SECURITY.md"
)

$errors = [System.Collections.Generic.List[string]]::new()

foreach ($relativePath in $requiredFiles) {
    $fullPath = Join-Path $repositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        $errors.Add("Required documentation file is missing: $relativePath")
        continue
    }

    if ((Get-Item -LiteralPath $fullPath).Length -eq 0) {
        $errors.Add("Documentation file is empty: $relativePath")
    }
}

$readmePath = Join-Path $repositoryRoot "README.md"
if (Test-Path -LiteralPath $readmePath -PathType Leaf) {
    $readme = Get-Content -LiteralPath $readmePath -Raw
    $requiredReadmeLinks = @(
        "docs/05-processing-pipeline-and-cap.md",
        "docs/10-configuration-and-secrets.md",
        "docs/11-mvp-local-run.md",
        "docs/15-authentication.md"
    )

    foreach ($target in $requiredReadmeLinks) {
        $escapedTarget = [Regex]::Escape($target)
        if ($readme -notmatch "\($escapedTarget(?:#[^)]+)?\)") {
            $errors.Add("README.md must contain a Markdown link to $target")
        }
    }
}

$markdownFiles = @()
$markdownFiles += Get-Item -LiteralPath $readmePath -ErrorAction SilentlyContinue
$markdownFiles += Get-Item -LiteralPath (Join-Path $repositoryRoot "SECURITY.md") -ErrorAction SilentlyContinue
$markdownFiles += Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "docs") -Filter "*.md" -File -Recurse

$githubPath = Join-Path $repositoryRoot ".github"
if (Test-Path -LiteralPath $githubPath -PathType Container) {
    $markdownFiles += Get-ChildItem -LiteralPath $githubPath -Filter "*.md" -File -Recurse
}

$linkPattern = [Regex]::new('!?(?:\[[^\]]*\])\((?<target>[^)\s]+)(?:\s+"[^"]*")?\)')
foreach ($markdownFile in $markdownFiles) {
    $content = Get-Content -LiteralPath $markdownFile.FullName -Raw
    foreach ($match in $linkPattern.Matches($content)) {
        $target = $match.Groups["target"].Value.Trim('<', '>')
        if ($target -match '^(?:[a-z][a-z0-9+.-]*:|#)' ) {
            continue
        }

        $pathWithoutFragment = ($target -split '#', 2)[0]
        if ([string]::IsNullOrWhiteSpace($pathWithoutFragment)) {
            continue
        }

        $decodedPath = [Uri]::UnescapeDataString($pathWithoutFragment)
        if ($decodedPath.StartsWith('/')) {
            $resolvedPath = Join-Path $repositoryRoot $decodedPath.TrimStart('/')
        } else {
            $resolvedPath = Join-Path $markdownFile.DirectoryName $decodedPath
        }

        if (-not (Test-Path -LiteralPath $resolvedPath)) {
            $relativeSource = [IO.Path]::GetRelativePath($repositoryRoot, $markdownFile.FullName)
            $errors.Add("Broken local link in ${relativeSource}: $target")
        }
    }
}

if ($errors.Count -gt 0) {
    foreach ($message in $errors) {
        Write-Error $message
    }

    throw "Documentation validation failed with $($errors.Count) error(s)."
}

Write-Host "Documentation validation passed ($($markdownFiles.Count) Markdown files checked)." -ForegroundColor Green
