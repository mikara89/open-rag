# OpenRAG local validation script
# Runs the same restore, dependency audit, Release build/test/coverage, and format checks as CI.
param(
    [switch]$SkipFormat,
    [string]$Configuration = "Release",
    [string]$ResultsDirectory = "artifacts/test-results"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSCommandPath | Split-Path -Parent
Set-Location $root

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  OpenRAG Validation" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$solution = "OpenRAG.slnx"
$resultsCandidate = if ([IO.Path]::IsPathRooted($ResultsDirectory)) {
    $ResultsDirectory
} else {
    Join-Path $root $ResultsDirectory
}

$resolvedResultsDirectory = [IO.Path]::GetFullPath($resultsCandidate)
$allowedResultsRoot = [IO.Path]::GetFullPath((Join-Path $root "artifacts"))
$allowedPrefix = $allowedResultsRoot + [IO.Path]::DirectorySeparatorChar
if (-not $resolvedResultsDirectory.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "ResultsDirectory must be a child of '$allowedResultsRoot'."
}

if (Test-Path -LiteralPath $resolvedResultsDirectory -PathType Container) {
    Remove-Item -LiteralPath $resolvedResultsDirectory -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $resolvedResultsDirectory | Out-Null

# 1. Restore
Write-Host "[1/6] dotnet restore..." -ForegroundColor Yellow
dotnet restore $solution
if ($LASTEXITCODE -ne 0) { throw "Restore failed" }

# 2. Dependency audit
Write-Host "[2/6] NuGet dependency audit..." -ForegroundColor Yellow
& (Join-Path $PSScriptRoot "dependency-audit.ps1")
if ($LASTEXITCODE -ne 0) { throw "Dependency audit failed" }

# 3. Dependency audit gate simulations
Write-Host "[3/6] Dependency audit gate simulations..." -ForegroundColor Yellow
& (Join-Path $PSScriptRoot "dependency-audit.tests.ps1")

# 4. Build
Write-Host "[4/6] dotnet build..." -ForegroundColor Yellow
dotnet build $solution --configuration $Configuration --no-restore -p:ContinuousIntegrationBuild=true
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

# 5. Test
Write-Host "[5/6] dotnet test..." -ForegroundColor Yellow
dotnet test $solution `
    --configuration $Configuration `
    --no-build `
    --logger "trx" `
    --results-directory $resolvedResultsDirectory `
    --collect "XPlat Code Coverage"
if ($LASTEXITCODE -ne 0) { throw "Tests failed" }

$trxFiles = @(Get-ChildItem -LiteralPath $resolvedResultsDirectory -Filter "*.trx" -File -Recurse)
if ($trxFiles.Count -eq 0) { throw "Tests passed but no TRX results were generated" }

$coverageFiles = @(
    Get-ChildItem -LiteralPath $resolvedResultsDirectory -Directory |
        ForEach-Object {
            Get-ChildItem -LiteralPath $_.FullName -Filter "coverage.cobertura.xml" -File
        }
)
if ($coverageFiles.Count -eq 0) { throw "Tests passed but no coverage results were generated" }

Write-Host "      TRX files: $($trxFiles.Count)" -ForegroundColor DarkGray
Write-Host "      Coverage files: $($coverageFiles.Count)" -ForegroundColor DarkGray

# 6. Format check
if (-not $SkipFormat) {
    Write-Host "[6/6] dotnet format whitespace --verify-no-changes..." -ForegroundColor Yellow
    dotnet format whitespace $solution --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Whitespace format check failed" }

    Write-Host "      dotnet format style --verify-no-changes..." -ForegroundColor Yellow
    dotnet format style $solution --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Style format check failed" }
} else {
    Write-Host "[6/6] Format check skipped (--SkipFormat)" -ForegroundColor DarkYellow
}

Write-Host "========================================" -ForegroundColor Green
Write-Host "  Validation complete" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
