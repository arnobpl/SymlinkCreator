#Requires -Version 7.4

[CmdletBinding()]
param(
    [string] $TargetPlatform = $env:TARGET_PLATFORM,
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $Verify,
    [switch] $Fix
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-DotNetFormatStyle {
    [CmdletBinding()]
    param(
        [switch] $VerifyNoChanges
    )

    $previousPlatform = $env:Platform
    $previousConfiguration = $env:Configuration
    try {
        $env:Platform = $TargetPlatform
        $env:Configuration = $Configuration

        $arguments = @('format', 'style', '.\SymlinkCreator.sln', '--no-restore')
        if ($VerifyNoChanges) {
            $arguments += '--verify-no-changes'
        }

        $arguments += @('--severity', 'info', '--verbosity', 'minimal')
        $formatOutput = [System.Collections.Generic.List[object]]::new()
        & dotnet @arguments 2>&1 |
            ForEach-Object {
                $formatOutput.Add($_)
                Write-Output $_
            }
        $formatExitCode = $LASTEXITCODE
    }
    finally {
        $env:Platform = $previousPlatform
        $env:Configuration = $previousConfiguration
    }

    if ($formatExitCode -ne 0) {
        throw "dotnet format style failed with exit code $formatExitCode."
    }

    # dotnet format can return zero after failing to load a project, leaving
    # its analyzers with incomplete workspace data. Treat those diagnostics as
    # a failure instead of reporting a false success.
    $workspaceLoadDiagnostics = @(
        $formatOutput |
            ForEach-Object { $_.ToString() } |
            Where-Object {
                $_ -match '^\s*Warnings were encountered while loading the workspace\.' -or
                $_ -match '^\s*MSBuild failed when processing the file .* with message:' -or
                $_ -match '^\s*Required references did not load' -or
                $_ -match '^\s*Found project reference without a matching metadata reference:'
            }
    )
    if ($workspaceLoadDiagnostics.Count -ne 0) {
        $diagnosticSummary = $workspaceLoadDiagnostics -join [Environment]::NewLine
        throw "dotnet format style could not load the solution workspace:`n$diagnosticSummary"
    }
}

function Invoke-SolutionRestore {
    [CmdletBinding()]
    param()

    & dotnet restore .\SymlinkCreator.sln `
        -p:Configuration=$Configuration `
        -p:Platform=$TargetPlatform
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed with exit code $LASTEXITCODE."
    }
}

function Invoke-SolutionBuild {
    [CmdletBinding()]
    param()

    & dotnet build .\SymlinkCreator.sln `
        --configuration $Configuration `
        --no-restore `
        -p:Platform=$TargetPlatform
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }
}

$scriptSupportModulePath = Join-Path $PSScriptRoot 'ScriptSupport.psm1'
Import-Module -Name $scriptSupportModulePath -Force
$hostPlatform = Get-HostPlatform

if ($Verify -and $Fix) {
    throw 'Specify either -Verify or -Fix, not both.'
}

if ([string]::IsNullOrWhiteSpace($TargetPlatform)) {
    if ($null -eq $hostPlatform) {
        throw "Unsupported operating-system architecture '$([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture)'."
    }

    $TargetPlatform = $hostPlatform
}

$TargetPlatform = switch ($TargetPlatform.Trim().ToUpperInvariant()) {
    'X64' { 'x64'; break }
    'ARM64' { 'ARM64'; break }
    default { throw "Unsupported platform '$TargetPlatform'. Supported platforms are: x64, ARM64." }
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$temporaryDrive = $null
if ($Verify -and $repositoryRoot.Contains('#', [StringComparison]::Ordinal)) {
    $driveLetter = 90..68 |
        ForEach-Object { [char] $_ } |
        Where-Object { $null -eq (Get-PSDrive -Name $_ -ErrorAction SilentlyContinue) } |
        Select-Object -First 1
    if ($null -eq $driveLetter) {
        throw 'C# verification requires an available drive letter when the repository path contains #.'
    }

    $temporaryDrive = "${driveLetter}:"
    & subst.exe $temporaryDrive $repositoryRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Could not map '$repositoryRoot' to temporary drive $temporaryDrive."
    }

    $repositoryRoot = "$temporaryDrive\"
    Write-Information "Using $temporaryDrive for C# verification because Roslyn namespace analysis ignores paths containing #." -InformationAction Continue
}

$projectPath = Join-Path $repositoryRoot 'SymlinkCreator.UI\SymlinkCreator.UI.csproj'

Push-Location $repositoryRoot
try {
    if ($Fix) {
        Write-Information "Fixing $Configuration for $TargetPlatform..." -InformationAction Continue

        Write-Information 'Restoring projects for formatting...' -InformationAction Continue
        Invoke-SolutionRestore

        Write-Information 'Formatting PowerShell scripts...' -InformationAction Continue
        Import-Module PSScriptAnalyzer -Force
        $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
        $scriptFiles = Get-ChildItem -LiteralPath .\scripts -Recurse -File |
            Where-Object { $_.Extension -in '.ps1', '.psm1' }
        foreach ($scriptFile in $scriptFiles) {
            $originalContent = [System.IO.File]::ReadAllText($scriptFile.FullName)
            $formattedContent = Invoke-Formatter -ScriptDefinition $originalContent
            if ($formattedContent -cne $originalContent) {
                [System.IO.File]::WriteAllText($scriptFile.FullName, $formattedContent, $utf8NoBom)
                Write-Information "Formatted $($scriptFile.FullName)" -InformationAction Continue
            }
        }

        Write-Information 'Formatting C# whitespace...' -InformationAction Continue
        & dotnet format whitespace . --folder --verbosity minimal
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet format whitespace failed with exit code $LASTEXITCODE."
        }

        Write-Information 'Building projects for style analysis...' -InformationAction Continue
        Invoke-SolutionBuild

        Write-Information 'Formatting C# style...' -InformationAction Continue
        Invoke-DotNetFormatStyle

        Write-Information 'Fix pass complete. Run .\scripts\Build.ps1 -Verify to validate the result.' -InformationAction Continue
        return
    }

    if (-not $Verify) {
        Write-Information "Building $Configuration for $TargetPlatform..." -InformationAction Continue
        & dotnet build $projectPath `
            --configuration $Configuration `
            -p:Platform=$TargetPlatform
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE."
        }

        return
    }

    Write-Information "Validating $Configuration for $TargetPlatform..." -InformationAction Continue

    Write-Information 'Linting PowerShell scripts...' -InformationAction Continue
    Import-Module PSScriptAnalyzer -Force
    $analysisResults = @(Invoke-ScriptAnalyzer -Path .\scripts -Recurse -Severity Warning, Error)
    if ($analysisResults.Count -ne 0) {
        $analysisSummary = $analysisResults | Format-Table -AutoSize | Out-String
        throw "PowerShell linting failed:`n$analysisSummary"
    }

    Write-Information 'Verifying C# formatting...' -InformationAction Continue
    & dotnet format whitespace . --folder --verify-no-changes --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet format failed with exit code $LASTEXITCODE."
    }

    Write-Information 'Restoring projects...' -InformationAction Continue
    Invoke-SolutionRestore

    Write-Information 'Building and linting projects...' -InformationAction Continue
    Invoke-SolutionBuild

    Write-Information 'Verifying C# style...' -InformationAction Continue
    Invoke-DotNetFormatStyle -VerifyNoChanges

    if ($TargetPlatform -eq $hostPlatform) {
        Write-Information 'Running tests...' -InformationAction Continue
        & dotnet test .\SymlinkCreator.Tests\SymlinkCreator.Tests.csproj `
            --configuration $Configuration `
            --no-build `
            --no-restore `
            -p:Platform=$TargetPlatform `
            --logger 'console;verbosity=normal'
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet test failed with exit code $LASTEXITCODE."
        }
    }
    else {
        Write-Information "Skipping test execution because $TargetPlatform is cross-targeted from the $hostPlatform host." -InformationAction Continue
    }
}
finally {
    Pop-Location
    if ($null -ne $temporaryDrive) {
        & subst.exe $temporaryDrive /D
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Temporary drive $temporaryDrive could not be removed."
        }
    }
}
