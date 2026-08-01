[CmdletBinding(DefaultParameterSetName = "Live")]
param(
    [Parameter(Mandatory, ParameterSetName = "Fixture")]
    [string]$FixtureJson,

    [Parameter(ParameterSetName = "Fixture")]
    [string]$FixtureStandardError = "",

    [Parameter(ParameterSetName = "Fixture")]
    [int]$FixtureExitCode = 0
)

# Fail CI when NuGet cannot provide trustworthy vulnerability data or reports a
# High/Critical vulnerability. Moderate and Low findings remain non-blocking.
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root "OpenRAG.slnx"

if ($PSCmdlet.ParameterSetName -eq "Fixture") {
    $queryResult = [pscustomobject]@{
        ExitCode = $FixtureExitCode
        StdOut = $FixtureJson
        StdErr = $FixtureStandardError
    }
}
else {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = "dotnet"
    $startInfo.WorkingDirectory = $root
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    foreach ($argument in @(
            "package",
            "list",
            "--project", $solution,
            "--include-transitive",
            "--vulnerable",
            "--format", "json",
            "--output-version", "1"
        )) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "NuGet vulnerability query could not be started."
        }

        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()

        $queryResult = [pscustomobject]@{
            ExitCode = $process.ExitCode
            StdOut = $standardOutput.GetAwaiter().GetResult()
            StdErr = $standardError.GetAwaiter().GetResult()
        }
    }
    finally {
        $process.Dispose()
    }
}

if ($queryResult.ExitCode -ne 0) {
    $details = @($queryResult.StdErr, $queryResult.StdOut) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $detailText = if ($details.Count -gt 0) {
        " Details: $($details -join [Environment]::NewLine)"
    }
    else {
        ""
    }

    throw "NuGet vulnerability query failed with exit code $($queryResult.ExitCode).$detailText"
}

$diagnostics = "$($queryResult.StdOut)$([Environment]::NewLine)$($queryResult.StdErr)"
$unavailableDataCodes = @(
    [regex]::Matches($diagnostics, "\bNU190(?:0|5)\b", [Text.RegularExpressions.RegexOptions]::IgnoreCase) |
        ForEach-Object { $_.Value.ToUpperInvariant() } |
        Sort-Object -Unique
)
if ($unavailableDataCodes.Count -gt 0) {
    throw "NuGet vulnerability data is unavailable: detected $($unavailableDataCodes -join ', ')."
}

if (-not [string]::IsNullOrWhiteSpace($queryResult.StdErr)) {
    Write-Warning "NuGet vulnerability query wrote to stderr: $($queryResult.StdErr.Trim())"
}

if ([string]::IsNullOrWhiteSpace($queryResult.StdOut)) {
    throw "NuGet vulnerability query returned no JSON output."
}

try {
    $report = $queryResult.StdOut | ConvertFrom-Json -Depth 100
}
catch {
    throw "NuGet vulnerability query returned malformed JSON: $($_.Exception.Message)"
}

if ($null -eq $report -or
    $report.PSObject.Properties.Name -notcontains "version" -or
    $report.PSObject.Properties.Name -notcontains "projects" -or
    @($report.projects).Count -eq 0) {
    throw "NuGet vulnerability query returned an incomplete JSON report."
}

$findings = @(
    foreach ($project in @($report.projects)) {
        $projectPath = [string]$project.path
        $projectName = if ([string]::IsNullOrWhiteSpace($projectPath)) {
            "<not supplied>"
        }
        elseif ([IO.Path]::IsPathRooted($projectPath)) {
            [IO.Path]::GetRelativePath($root, $projectPath)
        }
        else {
            $projectPath
        }

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
                        if ([string]::IsNullOrWhiteSpace($advisory)) {
                            $advisory = "<not supplied>"
                        }

                        [pscustomobject]@{
                            Project = $projectName
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
