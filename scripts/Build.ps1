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
        & dotnet restore .\SymlinkCreator.sln `
            -p:Configuration=$Configuration `
            -p:Platform=$TargetPlatform
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet restore failed with exit code $LASTEXITCODE."
        }

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

        Write-Information 'Formatting C# style...' -InformationAction Continue
        $previousPlatform = $env:Platform
        $previousConfiguration = $env:Configuration
        try {
            $env:Platform = $TargetPlatform
            $env:Configuration = $Configuration
            & dotnet format style .\SymlinkCreator.sln `
                --no-restore `
                --severity info `
                --verbosity minimal
            $styleExitCode = $LASTEXITCODE
        }
        finally {
            $env:Platform = $previousPlatform
            $env:Configuration = $previousConfiguration
        }
        if ($styleExitCode -ne 0) {
            throw "C# style formatting failed with exit code $styleExitCode."
        }

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
    & dotnet restore .\SymlinkCreator.sln `
        -p:Configuration=$Configuration `
        -p:Platform=$TargetPlatform
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed with exit code $LASTEXITCODE."
    }

    Write-Information 'Building and linting projects...' -InformationAction Continue
    & dotnet build .\SymlinkCreator.sln `
        --configuration $Configuration `
        --no-restore `
        -p:Platform=$TargetPlatform
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }

    Write-Information 'Verifying C# style...' -InformationAction Continue
    $previousPlatform = $env:Platform
    $previousConfiguration = $env:Configuration
    try {
        $env:Platform = $TargetPlatform
        $env:Configuration = $Configuration
        & dotnet format style .\SymlinkCreator.sln `
            --no-restore `
            --verify-no-changes `
            --severity info `
            --verbosity minimal
        $styleExitCode = $LASTEXITCODE
    }
    finally {
        $env:Platform = $previousPlatform
        $env:Configuration = $previousConfiguration
    }
    if ($styleExitCode -ne 0) {
        throw "C# style verification failed with exit code $styleExitCode."
    }

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
