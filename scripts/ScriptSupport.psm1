#Requires -Version 7.4

Set-StrictMode -Version Latest

$script:WinGetManifestValidationWarning = [int32]0x8A150028

function Get-HostPlatform {
    [CmdletBinding()]
    param()

    switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
        ([System.Runtime.InteropServices.Architecture]::Arm64) { return 'ARM64' }
        ([System.Runtime.InteropServices.Architecture]::X64) { return 'x64' }
        default { return $null }
    }
}

function Get-ProjectVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $BuildPropsPath
    )

    if (-not (Test-Path -LiteralPath $BuildPropsPath -PathType Leaf)) {
        throw "The project properties file was not found at '$BuildPropsPath'."
    }

    [xml] $buildProps = Get-Content -LiteralPath $BuildPropsPath -Raw
    $projectVersion = @(
        $buildProps.Project.PropertyGroup |
        Where-Object { $_.Version } |
        ForEach-Object { [string] $_.Version }
    )[0]
    if ([string]::IsNullOrWhiteSpace($projectVersion)) {
        throw "The project version could not be read from '$BuildPropsPath'."
    }

    return $projectVersion
}

function Invoke-WinGetManifestValidation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $ManifestDirectory
    )

    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw 'winget is required to validate the generated local manifest.'
    }

    & winget validate --manifest $ManifestDirectory
    $validationExitCode = $LASTEXITCODE
    if ($validationExitCode -eq $script:WinGetManifestValidationWarning) {
        Write-Information 'WinGet manifest validation succeeded with warnings.' -InformationAction Continue
    }
    elseif ($validationExitCode -ne 0) {
        throw "WinGet manifest validation failed with exit code $validationExitCode."
    }

    $global:LASTEXITCODE = 0
}

Export-ModuleMember -Function Get-HostPlatform, Get-ProjectVersion, Invoke-WinGetManifestValidation
