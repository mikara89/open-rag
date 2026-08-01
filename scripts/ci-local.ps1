# OpenRAG local CI entrypoint.
# Runs the same dependency audit, Release build/test/coverage/format, and documentation checks as GitHub Actions.
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot

& (Join-Path $PSScriptRoot "verify.ps1")
& (Join-Path $PSScriptRoot "docs-check.ps1")

Write-Host "Local CI checks passed for $repositoryRoot" -ForegroundColor Green
