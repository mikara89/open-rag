[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot "tests/OpenRAG.LiveIntegrationTests/OpenRAG.LiveIntegrationTests.csproj"
$results = Join-Path $repositoryRoot "artifacts/live-test-results"

try {
    docker info --format "{{.ServerVersion}}" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Docker is not available. Start Docker and retry."
    }

    New-Item -ItemType Directory -Path $results -Force | Out-Null
    $arguments = @(
        "test",
        $project,
        "--configuration", $Configuration,
        "--logger", "trx",
        "--results-directory", $results,
        "--collect", "XPlat Code Coverage"
    )
    if ($NoBuild) {
        $arguments += @("--no-build", "--no-restore")
    }

    & dotnet @arguments
    $testExitCode = $LASTEXITCODE
}
catch {
    Write-Error $_
    $testExitCode = 1
}
finally {
    Write-Host "Live test results: $results"
}

exit $testExitCode
