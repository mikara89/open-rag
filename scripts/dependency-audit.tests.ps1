# Deterministic smoke tests for the NuGet vulnerability policy gate.
$ErrorActionPreference = "Stop"

$auditScript = Join-Path $PSScriptRoot "dependency-audit.ps1"
$projectPath = Join-Path (Split-Path -Parent $PSScriptRoot) "src/OpenRAG.Api/OpenRAG.Api.csproj"

function New-AuditReport {
    param(
        [string]$Severity,
        [string]$Package = "Example.Package"
    )

    $framework = [ordered]@{ framework = "net10.0" }
    if (-not [string]::IsNullOrWhiteSpace($Severity)) {
        $framework.transitivePackages = @(
            [ordered]@{
                id = $Package
                resolvedVersion = "1.0.0"
                vulnerabilities = @(
                    [ordered]@{
                        severity = $Severity
                        advisoryurl = "https://example.invalid/advisories/TEST-1"
                    }
                )
            }
        )
    }

    [ordered]@{
        version = 1
        projects = @(
            [ordered]@{
                path = $projectPath
                frameworks = @($framework)
            }
        )
    } | ConvertTo-Json -Depth 10
}

function Invoke-AuditCase {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$Json,

        [string]$StandardError = "",
        [int]$ExitCode = 0,
        [bool]$ShouldFail = $false,
        [string[]]$ExpectedText = @()
    )

    $output = [Collections.Generic.List[string]]::new()
    $failure = $null
    try {
        & $auditScript `
            -FixtureJson $Json `
            -FixtureStandardError $StandardError `
            -FixtureExitCode $ExitCode *>&1 |
            ForEach-Object { $output.Add($_.ToString()) }
    }
    catch {
        $failure = $_
        $output.Add($_.ToString())
    }

    if ($ShouldFail -and $null -eq $failure) {
        throw "Audit simulation '$Name' passed but was expected to fail."
    }
    if (-not $ShouldFail -and $null -ne $failure) {
        throw "Audit simulation '$Name' failed unexpectedly: $failure"
    }

    $outputText = $output -join [Environment]::NewLine
    foreach ($expected in $ExpectedText) {
        if (-not $outputText.Contains($expected, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Audit simulation '$Name' did not emit expected text '$expected'. Output: $outputText"
        }
    }

    Write-Host "PASS: $Name" -ForegroundColor Green
}

$cleanReport = New-AuditReport

Invoke-AuditCase `
    -Name "High finding blocks" `
    -Json (New-AuditReport -Severity "High" -Package "Example.High") `
    -ShouldFail $true `
    -ExpectedText @("Package=Example.High", "Version=1.0.0", "Severity=High", "High/Critical")

Invoke-AuditCase `
    -Name "Moderate finding reports and passes" `
    -Json (New-AuditReport -Severity "Moderate" -Package "Example.Moderate") `
    -ExpectedText @("Package=Example.Moderate", "Version=1.0.0", "Severity=Moderate", "non-blocking")

Invoke-AuditCase `
    -Name "NU1900 blocks" `
    -Json $cleanReport `
    -StandardError "warning NU1900: Error occurred while getting package vulnerability data." `
    -ShouldFail $true `
    -ExpectedText @("NU1900", "unavailable")

Invoke-AuditCase `
    -Name "NU1905 blocks" `
    -Json $cleanReport `
    -StandardError "warning NU1905: An audit source does not provide a vulnerability database." `
    -ShouldFail $true `
    -ExpectedText @("NU1905", "unavailable")

Invoke-AuditCase `
    -Name "Malformed JSON blocks" `
    -Json "{not-json" `
    -ShouldFail $true `
    -ExpectedText @("malformed JSON")

Invoke-AuditCase `
    -Name "Nonzero query exit blocks" `
    -Json $cleanReport `
    -StandardError "simulated command failure" `
    -ExitCode 17 `
    -ShouldFail $true `
    -ExpectedText @("exit code 17", "simulated command failure")

Invoke-AuditCase `
    -Name "No findings passes" `
    -Json $cleanReport `
    -ExpectedText @("no vulnerable packages")

Write-Host "Dependency audit gate simulations passed." -ForegroundColor Green
