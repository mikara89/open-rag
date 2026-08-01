# Fail CI when NuGet reports a High or Critical package vulnerability.
# Moderate and Low findings are reported without failing until policy is tightened.
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root "OpenRAG.slnx"

Push-Location $root
try {
    $jsonLines = @(
        dotnet package list `
            --project $solution `
            --include-transitive `
            --vulnerable `
            --format json `
            --output-version 1
    )
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet vulnerability query failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$jsonText = $jsonLines -join [Environment]::NewLine
try {
    $report = $jsonText | ConvertFrom-Json -Depth 100
}
catch {
    throw "NuGet vulnerability query returned malformed JSON: $($_.Exception.Message)"
}

$findings = @(
    foreach ($project in @($report.projects)) {
        foreach ($framework in @($project.frameworks)) {
            foreach ($packageGroup in "topLevelPackages", "transitivePackages") {
                $packages = $framework.$packageGroup
                if ($null -eq $packages) {
                    continue
                }

                foreach ($package in @($packages)) {
                    foreach ($vulnerability in @($package.vulnerabilities)) {
                        if ($null -eq $vulnerability) {
                            continue
                        }

                        $advisory = [string]$vulnerability.advisoryurl
                        if ([string]::IsNullOrWhiteSpace($advisory)) {
                            $advisory = [string]$vulnerability.id
                        }

                        [pscustomobject]@{
                            Project = [IO.Path]::GetRelativePath($root, [string]$project.path)
                            Package = [string]$package.id
                            Version = [string]$package.resolvedVersion
                            Severity = [string]$vulnerability.severity
                            Advisory = $advisory
                        }
                    }
                }
            }
        }
    }
)

if ($findings.Count -eq 0) {
    Write-Host "NuGet vulnerability audit passed: no vulnerable packages were reported." -ForegroundColor Green
    return
}

$blockingFindings = @()
foreach ($finding in $findings) {
    $message = "Project=$($finding.Project); Package=$($finding.Package); " +
        "Version=$($finding.Version); Severity=$($finding.Severity); Advisory=$($finding.Advisory)"

    switch ($finding.Severity.ToLowerInvariant()) {
        { $_ -in "high", "critical" } {
            Write-Error $message -ErrorAction Continue
            $blockingFindings += $finding
        }
        { $_ -in "low", "moderate" } {
            Write-Warning $message
        }
        default {
            throw "NuGet returned an unsupported vulnerability severity: '$($finding.Severity)'."
        }
    }
}

if ($blockingFindings.Count -gt 0) {
    throw "NuGet vulnerability audit failed: $($blockingFindings.Count) High/Critical finding(s)."
}

Write-Host "NuGet vulnerability audit passed with $($findings.Count) non-blocking finding(s)." -ForegroundColor Green
