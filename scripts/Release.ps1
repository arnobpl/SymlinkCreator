#Requires -Version 7.4

[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $ReleaseNotesPath,
    [switch] $LaunchForManualTest,
    [switch] $SkipWinGetValidation,
    [switch] $ReplaceExistingRelease,
    [switch] $Publish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$isWhatIf = $WhatIfPreference

# The preview covers external publication only; local packaging and validation must still run fully.
$WhatIfPreference = $false

if ($LaunchForManualTest -and $Publish) {
    throw 'Use -LaunchForManualTest for local testing or -Publish for publication; do not combine them.'
}
if ($SkipWinGetValidation -and $Publish) {
    throw 'WinGet validation cannot be skipped when publishing a release.'
}
if ($ReplaceExistingRelease -and -not $Publish) {
    throw 'Replacing an existing release requires -Publish.'
}
if (-not [string]::IsNullOrWhiteSpace($ReleaseNotesPath) -and -not $Publish) {
    throw 'Release notes can only be supplied with -Publish.'
}

$resolvedReleaseNotesPath = if ([string]::IsNullOrWhiteSpace($ReleaseNotesPath)) {
    $null
}
elseif ([System.IO.Path]::IsPathRooted($ReleaseNotesPath)) {
    [System.IO.Path]::GetFullPath($ReleaseNotesPath)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $ReleaseNotesPath))
}
if ($null -ne $resolvedReleaseNotesPath -and
    -not (Test-Path -LiteralPath $resolvedReleaseNotesPath -PathType Leaf)) {
    throw "Release notes file was not found at '$resolvedReleaseNotesPath'."
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repositoryName = 'arnobpl/SymlinkCreator'
$buildPropsPath = Join-Path $repositoryRoot 'Directory.Build.props'
$scriptSupportModulePath = Join-Path $PSScriptRoot 'ScriptSupport.psm1'
$buildScriptPath = Join-Path $repositoryRoot 'scripts\Build.ps1'
$applicationProjectPath = Join-Path $repositoryRoot 'SymlinkCreator.UI\SymlinkCreator.UI.csproj'
$launcherProjectPath = Join-Path $repositoryRoot 'SymlinkCreator.Launcher\SymlinkCreator.Launcher.csproj'
$wingetTemplateDirectory = Join-Path $repositoryRoot 'scripts\winget'

Import-Module -Name $scriptSupportModulePath -Force

$releaseVersion = Get-ProjectVersion -BuildPropsPath $buildPropsPath
$tag = "v$releaseVersion"
$headMetadata = @(& git -C $repositoryRoot show -s '--format=%H%n%ct' HEAD)
if ($LASTEXITCODE -ne 0 -or $headMetadata.Count -ne 2) {
    throw 'Unable to read the HEAD commit and timestamp used for release metadata.'
}
$headCommit = $headMetadata[0].Trim()
if ($headCommit -notmatch '^[0-9a-fA-F]{40,64}$') {
    throw "Git returned an invalid HEAD commit '$headCommit'."
}
$commitTimestampText = $headMetadata[1].Trim()
$commitTimestampSeconds = [long] 0
if (-not [long]::TryParse(
        $commitTimestampText,
        [System.Globalization.NumberStyles]::None,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref] $commitTimestampSeconds)) {
    throw "Git returned an invalid HEAD commit timestamp '$commitTimestampText'."
}
$archiveTimestamp = [System.DateTimeOffset]::FromUnixTimeSeconds($commitTimestampSeconds).ToUniversalTime()
# ZIP stores seconds at two-second precision; normalize once so repeated builds have identical metadata.
$archiveTimestamp = $archiveTimestamp.AddSeconds( - ($archiveTimestamp.Second % 2))
$minimumZipTimestamp = [System.DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [System.TimeSpan]::Zero)
$maximumZipTimestamp = [System.DateTimeOffset]::new(2107, 12, 31, 23, 59, 58, [System.TimeSpan]::Zero)
if ($archiveTimestamp -lt $minimumZipTimestamp -or $archiveTimestamp -gt $maximumZipTimestamp) {
    throw "The HEAD commit timestamp '$archiveTimestamp' is outside the ZIP timestamp range."
}

if ($Publish -and -not [string]::IsNullOrWhiteSpace($env:GITHUB_REF_NAME) -and $env:GITHUB_REF_NAME -ne $tag) {
    throw "The workflow tag '$($env:GITHUB_REF_NAME)' does not match the project release tag '$tag'."
}

$outputDirectoryPath = Join-Path $repositoryRoot 'artifacts'

$stagingDirectory = Join-Path $outputDirectoryPath ".release-staging-$([guid]::NewGuid().ToString('N'))"
$manifestDirectory = Join-Path $stagingDirectory "winget\$releaseVersion"
$finalManifestDirectory = Join-Path $outputDirectoryPath "winget\$releaseVersion"
$installerManifestName = 'ArnobPaul.SymlinkCreator.installer.yaml'
$releaseTargets = @(
    [pscustomobject] @{
        Platform           = 'x64'
        RuntimeIdentifier  = 'win-x64'
        WinGetArchitecture = 'x64'
        AssetName          = 'Symlink.Creator.x64.zip'
    },
    [pscustomobject] @{
        Platform           = 'ARM64'
        RuntimeIdentifier  = 'win-arm64'
        WinGetArchitecture = 'arm64'
        AssetName          = 'Symlink.Creator.arm64.zip'
    }
)
$hostPlatform = Get-HostPlatform
if ($LaunchForManualTest -and $null -eq $hostPlatform) {
    throw "Release launch testing is unsupported on '$([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture)'."
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $ArgumentList
    )

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "Command '$FilePath' failed with exit code $LASTEXITCODE."
    }
}

function Write-Utf8NoBomContent {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Content
    )

    [System.IO.File]::WriteAllText($Path, $Content, [System.Text.UTF8Encoding]::new($false))
}

function Read-WinGetTemplate {
    param(
        [Parameter(Mandatory)] [string] $TemplateName,
        [Parameter(Mandatory)] [hashtable] $Replacements
    )

    $templatePath = Join-Path $wingetTemplateDirectory $TemplateName
    if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) {
        throw "WinGet manifest template was not found at '$templatePath'."
    }

    $content = Get-Content -LiteralPath $templatePath -Raw
    foreach ($replacement in $Replacements.GetEnumerator()) {
        $content = $content.Replace($replacement.Key, [string] $replacement.Value)
    }
    if ($content -match '__[A-Z0-9_]+__') {
        throw "WinGet manifest template '$templatePath' contains an unreplaced placeholder."
    }

    return $content
}

function Resolve-WinGetCreate {
    $command = Get-Command wingetcreate -ErrorAction SilentlyContinue
    $wingetCreate = if ($null -eq $command) { $null } else { $command.Source }

    if ([string]::IsNullOrWhiteSpace($wingetCreate) -or -not (Test-Path -LiteralPath $wingetCreate -PathType Leaf)) {
        throw 'wingetcreate was not found on PATH. Install it and retry.'
    }
    return $wingetCreate
}

function Write-WinGetManifestSet {
    param(
        [Parameter(Mandatory)] [string] $Directory,
        [Parameter(Mandatory)] [object[]] $Packages
    )

    $x64Package = @($Packages | Where-Object Platform -EQ 'x64')
    $arm64Package = @($Packages | Where-Object Platform -EQ 'ARM64')
    if ($x64Package.Count -ne 1 -or $arm64Package.Count -ne 1) {
        throw 'WinGet manifest generation requires exactly one x64 package and one ARM64 package.'
    }

    if (Test-Path -LiteralPath $Directory) {
        Remove-Item -LiteralPath $Directory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Directory -Force | Out-Null

    $replacements = @{
        '__VERSION__'                = $releaseVersion
        '__X64_INSTALLER_URL__'      = $x64Package[0].InstallerUrl
        '__X64_INSTALLER_SHA256__'   = $x64Package[0].ZipSha256
        '__ARM64_INSTALLER_URL__'    = $arm64Package[0].InstallerUrl
        '__ARM64_INSTALLER_SHA256__' = $arm64Package[0].ZipSha256
    }
    $manifests = @(
        @{ Template = 'ArnobPaul.SymlinkCreator.yaml.template'; Output = 'ArnobPaul.SymlinkCreator.yaml' },
        @{ Template = 'ArnobPaul.SymlinkCreator.installer.yaml.template'; Output = $installerManifestName },
        @{ Template = 'ArnobPaul.SymlinkCreator.locale.en-US.yaml.template'; Output = 'ArnobPaul.SymlinkCreator.locale.en-US.yaml' }
    )
    foreach ($manifest in $manifests) {
        $content = Read-WinGetTemplate -TemplateName $manifest.Template -Replacements $replacements
        Write-Utf8NoBomContent -Path (Join-Path $Directory $manifest.Output) -Content $content
    }
}

function Compress-DeterministicZip {
    param(
        [Parameter(Mandatory)] [string[]] $Files,
        [Parameter(Mandatory)] [string] $DestinationPath,
        [Parameter(Mandatory)] [string] $BaseDirectory
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::Open($DestinationPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $Files) {
            $entryName = [System.IO.Path]::GetRelativePath($BaseDirectory, $file).Replace('\', '/')
            $entry = $archive.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $archiveTimestamp
            $inputStream = [System.IO.File]::OpenRead($file)
            try {
                $outputStream = $entry.Open()
                try {
                    $inputStream.CopyTo($outputStream)
                }
                finally {
                    $outputStream.Dispose()
                }
            }
            finally {
                $inputStream.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Invoke-ArchitecturePackaging {
    param([Parameter(Mandatory)] [pscustomobject] $Target)

    $publishDirectory = Join-Path $stagingDirectory "publish\$($Target.WinGetArchitecture)"
    $launcherPublishDirectory = Join-Path $stagingDirectory "launcher\$($Target.WinGetArchitecture)"
    $zipPath = Join-Path $stagingDirectory $Target.AssetName
    Write-Information "Publishing the $($Target.Platform) framework-dependent application..." -InformationAction Continue
    & dotnet publish $applicationProjectPath `
        --configuration Release `
        --runtime $Target.RuntimeIdentifier `
        -p:Platform=$($Target.Platform) `
        --output $publishDirectory | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish for $($Target.Platform) failed with exit code $LASTEXITCODE."
    }

    Write-Information "Publishing the $($Target.Platform) command launcher..." -InformationAction Continue
    & dotnet publish $launcherProjectPath `
        --configuration Release `
        --runtime $Target.RuntimeIdentifier `
        -p:Platform=$($Target.Platform) `
        --output $launcherPublishDirectory | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish for the $($Target.Platform) command launcher failed with exit code $LASTEXITCODE."
    }
    $launcherPath = Join-Path $launcherPublishDirectory 'SymlinkCreator.Launcher.exe'
    if (-not (Test-Path -LiteralPath $launcherPath -PathType Leaf)) {
        throw "The $($Target.Platform) command launcher was not published."
    }
    Copy-Item -LiteralPath $launcherPath -Destination $publishDirectory

    $publishEntries = @(Get-ChildItem -LiteralPath $publishDirectory -File -Recurse -Force | Sort-Object FullName)
    if ($publishEntries.Count -eq 0) {
        throw "The $($Target.Platform) publish directory did not contain any files."
    }
    $archiveNames = @(
        $publishEntries |
            ForEach-Object { [System.IO.Path]::GetRelativePath($publishDirectory, $_.FullName).Replace('\', '/') } |
            Sort-Object
    )
    foreach ($requiredName in @('SymlinkCreator.exe', 'SymlinkCreator.Launcher.exe', 'resources.pri')) {
        $requiredPath = Join-Path $publishDirectory $requiredName
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf) -or
            (Get-Item -LiteralPath $requiredPath).Length -le 0) {
            throw "Required $($Target.Platform) release file '$requiredName' is missing or empty."
        }
    }

    Compress-DeterministicZip `
        -Files @($publishEntries | ForEach-Object FullName) `
        -BaseDirectory $publishDirectory `
        -DestinationPath $zipPath

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $zipEntryNames = @($archive.Entries | ForEach-Object FullName | Sort-Object)
        $entriesWithUnexpectedTimestamp = @($archive.Entries | Where-Object {
                $_.LastWriteTime.Year -ne $archiveTimestamp.Year -or
                $_.LastWriteTime.Month -ne $archiveTimestamp.Month -or
                $_.LastWriteTime.Day -ne $archiveTimestamp.Day -or
                $_.LastWriteTime.Hour -ne $archiveTimestamp.Hour -or
                $_.LastWriteTime.Minute -ne $archiveTimestamp.Minute -or
                $_.LastWriteTime.Second -ne $archiveTimestamp.Second
            })
    }
    finally {
        $archive.Dispose()
    }
    if (($zipEntryNames -join "`n") -ne ($archiveNames -join "`n")) {
        throw "The $($Target.Platform) ZIP does not exactly match its publish directory."
    }
    if ($entriesWithUnexpectedTimestamp.Count -ne 0) {
        throw "The $($Target.Platform) ZIP contains non-deterministic entry timestamps."
    }

    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    $payloadBytes = ($publishEntries | Measure-Object -Property Length -Sum).Sum
    Write-Information ("{0}: {1} files, {2:N2} MB, SHA-256 {3}" -f `
            $Target.AssetName, $publishEntries.Count, ($payloadBytes / 1MB), $zipHash) `
        -InformationAction Continue

    return [pscustomobject] @{
        Platform           = $Target.Platform
        WinGetArchitecture = $Target.WinGetArchitecture
        AssetName          = $Target.AssetName
        ZipPath            = $zipPath
        ZipLength          = (Get-Item -LiteralPath $zipPath).Length
        ZipSha256          = $zipHash
        ArchiveNames       = $archiveNames
        InstallerUrl       = "https://github.com/$repositoryName/releases/download/$tag/$($Target.AssetName)"
    }
}

function Invoke-ReleasePackaging {
    New-Item -ItemType Directory -Path $outputDirectoryPath -Force | Out-Null
    New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

    Write-Information "Packaging Symlink Creator $releaseVersion for x64 and ARM64..." -InformationAction Continue
    Write-Information "Using Git HEAD timestamp $($archiveTimestamp.ToString('u')) for reproducible ZIP entries." -InformationAction Continue
    $validationPlatform = if ($null -eq $hostPlatform) { 'x64' } else { $hostPlatform }
    Write-Information "Validating source and running tests for the host-compatible $validationPlatform target..." -InformationAction Continue
    & $buildScriptPath -TargetPlatform $validationPlatform -Configuration Release -Verify | Out-Host

    $packages = @($releaseTargets | ForEach-Object { Invoke-ArchitecturePackaging -Target $_ })
    Write-WinGetManifestSet -Directory $manifestDirectory -Packages $packages
    Write-Information "Generated WinGet manifests: $manifestDirectory" -InformationAction Continue

    return [pscustomobject] @{
        Packages              = $packages
        ManifestDirectory     = $manifestDirectory
        InstallerManifestPath = Join-Path $manifestDirectory $installerManifestName
    }
}

function Test-ArchivePackage {
    param([Parameter(Mandatory)] [pscustomobject] $Package)

    if (-not (Test-Path -LiteralPath $Package.ZipPath -PathType Leaf)) {
        throw "Release ZIP was not created at '$($Package.ZipPath)'."
    }

    $manualTestRoot = Join-Path $outputDirectoryPath 'manual-test'
    $extractRoot = Join-Path $manualTestRoot "$($Package.WinGetArchitecture)-$([guid]::NewGuid().ToString('N'))"
    $shouldLaunch = $LaunchForManualTest -and $Package.Platform -eq $hostPlatform
    try {
        Expand-Archive -LiteralPath $Package.ZipPath -DestinationPath $extractRoot
        $entries = @(Get-ChildItem -LiteralPath $extractRoot -File -Recurse -Force)
        $actualNames = @(
            $entries |
                ForEach-Object { [System.IO.Path]::GetRelativePath($extractRoot, $_.FullName).Replace('\', '/') } |
                Sort-Object
        )
        $expectedNames = @($Package.ArchiveNames | Sort-Object)
        if (($actualNames -join "`n") -ne ($expectedNames -join "`n")) {
            throw "The extracted $($Package.Platform) ZIP does not exactly match its publish output."
        }
        foreach ($name in $expectedNames) {
            if ((Get-Item -LiteralPath (Join-Path $extractRoot $name)).Length -le 0) {
                throw "Extracted $($Package.Platform) release file '$name' is empty."
            }
        }

        $escapedVersion = [regex]::Escape($releaseVersion)
        $executablePath = Join-Path $extractRoot 'SymlinkCreator.exe'
        $launcherPath = Join-Path $extractRoot 'SymlinkCreator.Launcher.exe'
        foreach ($versionedExecutable in @($executablePath, $launcherPath)) {
            $fileVersion = (Get-Item -LiteralPath $versionedExecutable).VersionInfo.FileVersion
            if ($fileVersion -notmatch "^$escapedVersion(?:\.0)?$") {
                $executableName = Split-Path -Leaf $versionedExecutable
                throw "$($Package.Platform) $executableName FileVersion '$fileVersion' does not match project version '$releaseVersion'."
            }
        }

        Write-Information "$($Package.Platform) release ZIP and executable versions verified." -InformationAction Continue
        if ($shouldLaunch) {
            Write-Information "Fresh $($Package.Platform) extraction retained for manual testing: $extractRoot" -InformationAction Continue
            Start-Process -FilePath $launcherPath -WorkingDirectory $extractRoot | Out-Null
            Write-Information "Launched the host-compatible $($Package.Platform) release through its command launcher." -InformationAction Continue
        }
    }
    finally {
        if (-not $shouldLaunch -and (Test-Path -LiteralPath $extractRoot)) {
            Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
        if (-not $shouldLaunch -and (Test-Path -LiteralPath $manualTestRoot) -and
            @(Get-ChildItem -LiteralPath $manualTestRoot -Force).Count -eq 0) {
            Remove-Item -LiteralPath $manualTestRoot -Force -ErrorAction SilentlyContinue
        }
    }
}

function Test-ReleaseBundle {
    param([Parameter(Mandatory)] [pscustomobject] $Release)

    $escapedVersion = [regex]::Escape($releaseVersion)
    $installerManifest = Get-Content -LiteralPath $Release.InstallerManifestPath -Raw
    if ($installerManifest -notmatch "(?m)^PackageVersion: $escapedVersion\r?$") {
        throw "WinGet PackageVersion does not match project version '$releaseVersion'."
    }
    if ($installerManifest -notmatch '(?m)^- RelativeFilePath: SymlinkCreator\.Launcher\.exe\r?$' -or
        $installerManifest -notmatch '(?m)^  PortableCommandAlias: symlinkcreator\r?$') {
        throw 'WinGet must map the symlinkcreator alias through SymlinkCreator.Launcher.exe.'
    }
    foreach ($package in $Release.Packages) {
        if ($installerManifest -notmatch [regex]::Escape($package.InstallerUrl) -or
            $installerManifest -notmatch $package.ZipSha256) {
            throw "WinGet metadata does not match the $($package.Platform) release package."
        }
    }

    if ($SkipWinGetValidation) {
        Write-Information 'Skipping WinGet CLI validation because it was explicitly disabled.' -InformationAction Continue
    }
    else {
        Invoke-WinGetManifestValidation -ManifestDirectory $Release.ManifestDirectory
    }

    foreach ($package in $Release.Packages) {
        Test-ArchivePackage -Package $package
    }
}

function Complete-ReleaseBundle {
    param([Parameter(Mandatory)] [pscustomobject] $Release)

    foreach ($package in $Release.Packages) {
        $finalZipPath = Join-Path $outputDirectoryPath $package.AssetName
        if (Test-Path -LiteralPath $finalZipPath) {
            Remove-Item -LiteralPath $finalZipPath -Force
        }
        Move-Item -LiteralPath $package.ZipPath -Destination $finalZipPath
        $package.ZipPath = $finalZipPath
    }

    if (Test-Path -LiteralPath $finalManifestDirectory) {
        Remove-Item -LiteralPath $finalManifestDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $finalManifestDirectory) -Force | Out-Null
    Move-Item -LiteralPath $Release.ManifestDirectory -Destination $finalManifestDirectory
    $Release.ManifestDirectory = $finalManifestDirectory
    $Release.InstallerManifestPath = Join-Path $finalManifestDirectory $installerManifestName
    Write-Information "Release artifacts ready: $outputDirectoryPath" -InformationAction Continue
}

function Get-GitHubRelease {
    param([Parameter(Mandatory)] [string] $ReleaseTag)

    $response = @(& gh api "repos/$repositoryName/releases/tags/$ReleaseTag" --jq `
            '{tagName: .tag_name, assets: [.assets[] | {name, size, url: .browser_download_url}]}' 2>&1)
    $responseText = ($response | ForEach-Object ToString) -join "`n"
    if ($LASTEXITCODE -eq 0) {
        return $responseText | ConvertFrom-Json
    }
    if ($responseText -match '(?i)HTTP 404|"status"\s*:\s*"?404"?') {
        return $null
    }
    throw "Unable to inspect GitHub release '$ReleaseTag': $responseText"
}

function Get-GitHubTagCommit {
    $response = @(& gh api "repos/$repositoryName/commits/$tag" --jq '.sha' 2>&1)
    $responseText = ($response | ForEach-Object ToString) -join "`n"
    if ($LASTEXITCODE -eq 0) {
        $commit = $responseText.Trim()
        if ($commit -notmatch '^[0-9a-fA-F]{40,64}$') {
            throw "GitHub release tag '$tag' resolved to an invalid commit '$commit'."
        }
        return $commit
    }
    if ($responseText -match '(?i)HTTP 404|"status"\s*:\s*"?404"?') {
        return $null
    }
    throw "Unable to resolve GitHub release tag '$tag': $responseText"
}

function Invoke-GitHubReleaseTagReplacement {
    param([Parameter(Mandatory)] [string] $Commit)

    Invoke-CheckedCommand -FilePath 'gh' -ArgumentList @(
        'api',
        '--method', 'PATCH',
        "repos/$repositoryName/git/refs/tags/$tag",
        '-f', "sha=$Commit",
        '-F', 'force=true'
    )
    Write-Information "GitHub release tag $tag now points to $Commit." -InformationAction Continue
}

function Invoke-WinGetForkSync {
    $ownerResponse = @(& gh api user --jq '.login' 2>&1)
    $ownerResponseText = ($ownerResponse | ForEach-Object ToString) -join "`n"
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to identify the GitHub CLI user before synchronizing the WinGet fork: $ownerResponseText"
    }
    $forkOwner = $ownerResponseText.Trim()
    if ($forkOwner -notmatch '^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?$') {
        throw "GitHub CLI returned an invalid account name '$forkOwner'."
    }

    $forkRepository = "$forkOwner/winget-pkgs"
    $forkResponse = @(& gh api "repos/$forkRepository" --jq `
            '{isFork: .fork, parent: .parent.full_name, upstreamBranch: .parent.default_branch}' 2>&1)
    $forkResponseText = ($forkResponse | ForEach-Object ToString) -join "`n"
    if ($LASTEXITCODE -ne 0) {
        if ($forkResponseText -match '(?i)HTTP 404|"status"\s*:\s*"?404"?') {
            Write-Information "No $forkRepository fork exists yet; WingetCreate will create it during submission." -InformationAction Continue
            return
        }
        throw "Unable to inspect the WinGet fork '$forkRepository': $forkResponseText"
    }

    $fork = $forkResponseText | ConvertFrom-Json
    if (-not $fork.isFork -or $fork.parent -ne 'microsoft/winget-pkgs') {
        throw "GitHub repository '$forkRepository' is not a fork of microsoft/winget-pkgs."
    }

    Write-Information "Synchronizing $forkRepository with microsoft/winget-pkgs..." -InformationAction Continue
    Invoke-CheckedCommand -FilePath 'gh' -ArgumentList @(
        'repo', 'sync', $forkRepository,
        '--source', 'microsoft/winget-pkgs',
        '--branch', $fork.upstreamBranch
    )
}

function Assert-GitHubReleaseAsset {
    param(
        [Parameter(Mandatory)] [object] $GitHubRelease,
        [Parameter(Mandatory)] [pscustomobject] $Package
    )

    if ($GitHubRelease.tagName -ne $tag) {
        throw "GitHub release verification returned tag '$($GitHubRelease.tagName)' instead of '$tag'."
    }
    $asset = @($GitHubRelease.assets | Where-Object name -EQ $Package.AssetName)
    if ($asset.Count -ne 1) {
        throw "GitHub release verification did not find exactly one $($Package.AssetName) asset."
    }
    if ($asset[0].size -ne $Package.ZipLength) {
        throw "The $($Package.AssetName) asset size does not match the local ZIP."
    }

    $verificationDirectory = Join-Path $env:TEMP "SymlinkCreator-release-asset-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $verificationDirectory -Force | Out-Null
    try {
        Invoke-CheckedCommand -FilePath 'gh' -ArgumentList @(
            'release', 'download', $tag,
            '--repo', $repositoryName,
            '--pattern', $Package.AssetName,
            '--dir', $verificationDirectory
        )
        $downloadedHash = (Get-FileHash -LiteralPath (Join-Path $verificationDirectory $Package.AssetName) -Algorithm SHA256).Hash
        if ($downloadedHash -ne $Package.ZipSha256) {
            throw "The downloaded $($Package.AssetName) SHA-256 does not match the local ZIP."
        }
    }
    finally {
        if (Test-Path -LiteralPath $verificationDirectory) {
            Remove-Item -LiteralPath $verificationDirectory -Recurse -Force
        }
    }
    Write-Information "GitHub release asset verified: $($asset[0].url)" -InformationAction Continue
}

function Assert-CleanReleaseSource {
    $status = @(git -C $repositoryRoot status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect Git status before publication.'
    }
    if ($status.Count -ne 0) {
        throw "Publication requires a clean worktree. Changes found:`n$($status -join "`n")"
    }
}

function Publish-Release {
    param(
        [Parameter(Mandatory)] [pscustomobject] $Release,
        [Parameter(Mandatory)] [string] $WinGetCreate,
        [string] $NotesPath
    )

    $githubRelease = Get-GitHubRelease -ReleaseTag $tag
    $tagCommit = Get-GitHubTagCommit
    if ($null -ne $tagCommit -and $tagCommit -ne $headCommit) {
        if (-not $ReplaceExistingRelease) {
            throw "GitHub release tag '$tag' points to $tagCommit instead of HEAD $headCommit. Use -ReplaceExistingRelease to move the tag and replace its assets."
        }
        Invoke-GitHubReleaseTagReplacement -Commit $headCommit
    }
    elseif ($null -ne $githubRelease -and $null -eq $tagCommit) {
        throw "GitHub release '$tag' exists without a resolvable Git tag."
    }

    if ($null -eq $githubRelease) {
        Write-Information "Creating GitHub release $tag..." -InformationAction Continue
        $createArguments = @('release', 'create', $tag)
        $createArguments += @($Release.Packages | ForEach-Object ZipPath)
        $createArguments += @(
            '--repo', $repositoryName,
            '--target', $headCommit,
            '--title', "Symlink Creator $releaseVersion"
        )
        if ([string]::IsNullOrWhiteSpace($NotesPath)) {
            $createArguments += @('--notes', "Portable x64 and ARM64 releases of Symlink Creator $releaseVersion.")
        }
        else {
            $createArguments += @('--notes-file', $NotesPath)
        }
        Invoke-CheckedCommand -FilePath 'gh' -ArgumentList $createArguments
    }
    else {
        if ($ReplaceExistingRelease) {
            Write-Information "Replacing GitHub release $tag with the validated build from $headCommit..." -InformationAction Continue
            $packagesToUpload = @($Release.Packages)
        }
        else {
            Write-Information "GitHub release $tag already exists; verifying it so publication can resume safely." -InformationAction Continue
            $packagesToUpload = @($Release.Packages | Where-Object {
                    $assetName = $_.AssetName
                    @($githubRelease.assets | Where-Object name -EQ $assetName).Count -eq 0
                })
        }

        foreach ($package in $packagesToUpload) {
            Write-Information "Uploading $($package.AssetName)..." -InformationAction Continue
            $uploadArguments = @(
                'release', 'upload', $tag, $package.ZipPath,
                '--repo', $repositoryName
            )
            if ($ReplaceExistingRelease) {
                $uploadArguments += '--clobber'
            }
            Invoke-CheckedCommand -FilePath 'gh' -ArgumentList $uploadArguments
        }

        if (-not [string]::IsNullOrWhiteSpace($NotesPath)) {
            Invoke-CheckedCommand -FilePath 'gh' -ArgumentList @(
                'release', 'edit', $tag,
                '--repo', $repositoryName,
                '--notes-file', $NotesPath
            )
            Write-Information "GitHub release notes updated from '$NotesPath'." -InformationAction Continue
        }
    }

    $githubRelease = Get-GitHubRelease -ReleaseTag $tag
    if ($null -eq $githubRelease) {
        throw "GitHub release '$tag' could not be found after publication."
    }
    $publishedTagCommit = Get-GitHubTagCommit
    if ($publishedTagCommit -ne $headCommit) {
        throw "Published tag '$tag' points to '$publishedTagCommit' instead of HEAD '$headCommit'."
    }
    foreach ($package in $Release.Packages) {
        Assert-GitHubReleaseAsset -GitHubRelease $githubRelease -Package $package
    }
    Invoke-WinGetForkSync
    Write-Information 'Submitting the WinGet manifest PR (using WingetCreate cached OAuth or WINGET_CREATE_GITHUB_TOKEN)...' -InformationAction Continue
    Invoke-CheckedCommand -FilePath $WinGetCreate -ArgumentList @(
        'submit',
        '--prtitle', "Add ArnobPaul.SymlinkCreator $releaseVersion",
        '--no-open',
        $Release.ManifestDirectory
    )
    Write-Information 'WinGet manifest PR submission completed.' -InformationAction Continue
}

Push-Location $repositoryRoot
try {
    $wingetCreate = $null
    if ($Publish) {
        Assert-CleanReleaseSource
        if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
            throw 'GitHub CLI (gh) is required to create the GitHub release.'
        }
        $wingetCreate = Resolve-WinGetCreate
    }

    $release = Invoke-ReleasePackaging
    Test-ReleaseBundle -Release $release
    if ($isWhatIf) {
        Write-Information 'What if: Skipping promotion of release artifacts.' -InformationAction Continue
    }
    else {
        Complete-ReleaseBundle -Release $release
    }

    if ($Publish) {
        $WhatIfPreference = $isWhatIf
        try {
            if ($PSCmdlet.ShouldProcess(
                    "$repositoryName and microsoft/winget-pkgs",
                    "publish GitHub release $tag and submit its WinGet manifest")) {
                Publish-Release -Release $release -WinGetCreate $wingetCreate -NotesPath $resolvedReleaseNotesPath
            }
        }
        finally {
            $WhatIfPreference = $false
        }
        return
    }
    if (-not $LaunchForManualTest) {
        Write-Information 'External publication not requested. Use -Publish to create the GitHub release and submit the WinGet PR.' -InformationAction Continue
    }
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
    $WhatIfPreference = $isWhatIf
    Pop-Location
}
