#Requires -Version 7.4

[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)]
    [int] $Port = 8765
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseScriptPath = Join-Path $repositoryRoot 'scripts\Release.ps1'
$buildPropsPath = Join-Path $repositoryRoot 'Directory.Build.props'
$scriptSupportModulePath = Join-Path $PSScriptRoot 'ScriptSupport.psm1'
$outputDirectoryPath = Join-Path $repositoryRoot 'artifacts'

Import-Module -Name $scriptSupportModulePath -Force

$releaseVersion = Get-ProjectVersion -BuildPropsPath $buildPropsPath
$manifestDirectory = Join-Path $outputDirectoryPath "winget\$releaseVersion"
$installerManifestPath = Join-Path $manifestDirectory 'ArnobPaul.SymlinkCreator.installer.yaml'
$localManifestBaseUrl = "http://127.0.0.1:$Port"
$server = $null
$originalInstallerManifestBytes = $null

function Get-InstallerAssetList {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "The generated installer manifest was not found at '$Path'."
    }

    $urlMatches = @(
        [regex]::Matches(
            (Get-Content -LiteralPath $Path -Raw),
            '(?m)^\s*InstallerUrl:\s*(?<url>\S+)\s*$')
    )
    if ($urlMatches.Count -eq 0) {
        throw "The generated installer manifest at '$Path' contains no installer URLs."
    }

    $assetNames = @(
        foreach ($match in $urlMatches) {
            $installerUrl = $match.Groups['url'].Value
            try {
                $installerUri = [Uri] $installerUrl
            }
            catch {
                throw "The generated installer URL '$installerUrl' is invalid."
            }

            if (-not $installerUri.IsAbsoluteUri) {
                throw "The generated installer URL '$installerUrl' is not absolute."
            }

            $assetName = [System.IO.Path]::GetFileName($installerUri.AbsolutePath)
            if (
                [string]::IsNullOrWhiteSpace($assetName) -or
                -not $assetName.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase)) {
                throw "The generated installer URL '$installerUrl' does not identify a ZIP asset."
            }
            $assetName
        }
    )
    if (@($assetNames | Sort-Object -Unique).Count -ne $assetNames.Count) {
        throw 'The generated installer manifest contains duplicate ZIP asset names.'
    }

    foreach ($assetName in $assetNames) {
        $assetPath = Join-Path $outputDirectoryPath $assetName
        if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
            throw "The generated ZIP asset was not found at '$assetPath'."
        }
    }

    return $assetNames
}

function Set-InstallerManifestForLocalTesting {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $BaseUrl,
        [Parameter(Mandatory)] [string[]] $AssetNames
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "The generated installer manifest was not found at '$Path'."
    }

    $lines = [regex]::Split((Get-Content -LiteralPath $Path -Raw), '\r?\n')
    $installerIndex = 0
    for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
        if ($lines[$lineIndex] -notmatch '^(?<indent>\s*)InstallerUrl:\s*\S+\s*$') {
            continue
        }

        if ($installerIndex -ge $AssetNames.Count) {
            throw "The generated installer manifest contains more than $($AssetNames.Count) installer URLs."
        }

        $indent = $matches['indent']
        $assetName = $AssetNames[$installerIndex]
        $lines[$lineIndex] = "$indent" + "InstallerUrl: $BaseUrl/$assetName"
        $installerIndex++
    }

    if ($installerIndex -ne $AssetNames.Count) {
        throw "The generated installer manifest contains $installerIndex installer URLs; expected $($AssetNames.Count)."
    }

    if ($PSCmdlet.ShouldProcess($Path, 'Rewrite installer URLs for local testing')) {
        [System.IO.File]::WriteAllText(
            $Path,
            ($lines -join [Environment]::NewLine),
            [System.Text.UTF8Encoding]::new($false))
    }
}

function Start-LocalZipServer {
    [CmdletBinding(SupportsShouldProcess)]
    param()

    if (-not $PSCmdlet.ShouldProcess(
            $localManifestBaseUrl,
            "serve local release artifacts from $outputDirectoryPath")) {
        return
    }

    $pythonCommand = Get-Command py -ErrorAction SilentlyContinue
    if ($null -eq $pythonCommand) {
        $pythonCommand = Get-Command python -ErrorAction SilentlyContinue
    }
    if ($null -eq $pythonCommand) {
        throw 'Local WinGet testing requires Python 3 (the py or python command) to serve the ZIPs.'
    }

    $process = Start-Process -FilePath $pythonCommand.Path -ArgumentList @(
        '-m',
        'http.server',
        [string] $Port,
        '--bind',
        '127.0.0.1',
        '--directory',
        $outputDirectoryPath
    ) -WorkingDirectory $outputDirectoryPath -WindowStyle Hidden -PassThru

    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        if ($process.HasExited) {
            break
        }

        $client = [System.Net.Sockets.TcpClient]::new()
        try {
            $client.Connect('127.0.0.1', $Port)
            return $process
        }
        catch {
            Write-Verbose "The local ZIP server is not ready yet: $($_.Exception.Message)"
        }
        finally {
            $client.Dispose()
        }

        Start-Sleep -Milliseconds 100
    }

    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
    }

    throw "The local ZIP server could not start on port $Port."
}

function Test-LocalZipUrl {
    param(
        [Parameter(Mandatory)] [string[]] $AssetNames
    )

    foreach ($assetName in $AssetNames) {
        $url = "$localManifestBaseUrl/$assetName"
        try {
            $response = Invoke-WebRequest -Uri $url -Method Head
        }
        catch {
            throw "The local ZIP URL '$url' could not be requested: $($_.Exception.Message)"
        }

        if ($response.StatusCode -ne 200) {
            throw "The local ZIP URL '$url' returned HTTP status $($response.StatusCode)."
        }
    }
}

try {
    Write-Information "Packaging Symlink Creator $releaseVersion for local WinGet testing..." -InformationAction Continue
    & $releaseScriptPath

    $installerAssetNames = Get-InstallerAssetList -Path $installerManifestPath
    $originalInstallerManifestBytes = [System.IO.File]::ReadAllBytes($installerManifestPath)
    $server = Start-LocalZipServer
    Set-InstallerManifestForLocalTesting `
        -Path $installerManifestPath `
        -BaseUrl $localManifestBaseUrl `
        -AssetNames $installerAssetNames
    Test-LocalZipUrl -AssetNames $installerAssetNames
    Invoke-WinGetManifestValidation -ManifestDirectory $manifestDirectory

    Write-Information "Local WinGet manifest: $manifestDirectory" -InformationAction Continue
    Write-Information "Local ZIP server is running at $localManifestBaseUrl." -InformationAction Continue
    Write-Information 'Run these commands in another elevated PowerShell terminal:' -InformationAction Continue
    Write-Information 'winget settings --enable LocalManifestFiles' -InformationAction Continue
    Write-Information "winget install --manifest `"$manifestDirectory`" --accept-source-agreements --accept-package-agreements" -InformationAction Continue
    Write-Information 'Use another terminal for the WinGet installation, then press Ctrl+C here to stop the server.' -InformationAction Continue
    Wait-Process -Id $server.Id
    if ($server.HasExited) {
        throw "The local ZIP server exited unexpectedly with code $($server.ExitCode)."
    }
}
finally {
    try {
        if ($null -ne $originalInstallerManifestBytes -and (Test-Path -LiteralPath $installerManifestPath -PathType Leaf)) {
            [System.IO.File]::WriteAllBytes($installerManifestPath, $originalInstallerManifestBytes)
        }
    }
    finally {
        if ($null -ne $server -and -not $server.HasExited) {
            Stop-Process -Id $server.Id -Force
        }
    }
}
