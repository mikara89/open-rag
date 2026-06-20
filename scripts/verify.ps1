# OpenRAG local validation script
# Runs restore, build, test, and format checks.
param(
    [switch]$SkipFormat
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSCommandPath | Split-Path -Parent
Set-Location $root

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  OpenRAG Validation" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$solution = "OpenRAG.slnx"

# 1. Restore
Write-Host "[1/4] dotnet restore..." -ForegroundColor Yellow
dotnet restore $solution
if ($LASTEXITCODE -ne 0) { throw "Restore failed" }

# 2. Build
Write-Host "[2/4] dotnet build..." -ForegroundColor Yellow
dotnet build $solution --no-restore
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

# 3. Test
Write-Host "[3/4] dotnet test..." -ForegroundColor Yellow
dotnet test $solution --no-build
if ($LASTEXITCODE -ne 0) { throw "Tests failed" }

# 4. Format check
if (-not $SkipFormat) {
    Write-Host "[4/4] dotnet format whitespace --verify-no-changes..." -ForegroundColor Yellow
    dotnet format whitespace $solution --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Whitespace format check failed" }

    Write-Host "      dotnet format style --verify-no-changes..." -ForegroundColor Yellow
    dotnet format style $solution --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Style format check failed" }
} else {
    Write-Host "[4/4] Format check skipped (--SkipFormat)" -ForegroundColor DarkYellow
}

Write-Host "========================================" -ForegroundColor Green
Write-Host "  Validation complete" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
