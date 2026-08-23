#Requires -Version 7.4

[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $OutputDirectory,
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string] $Repository = 'arnobpl/SymlinkCreator',
    [string] $WinGetCreatePath,
    [string] $ReleaseNotesPath,
    [switch] $LaunchForManualTest,
    [switch] $SkipWinGetValidation,
    [switch] $Publish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($LaunchForManualTest -and $Publish) {
    throw 'Use -LaunchForManualTest for local testing or -Publish for publication; do not combine them.'
}
if ($SkipWinGetValidation -and $Publish) {
    throw 'WinGet validation cannot be skipped when publishing a release.'
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
$buildPropsPath = Join-Path $repositoryRoot 'Directory.Build.props'
$buildScriptPath = Join-Path $repositoryRoot 'scripts\Build.ps1'
$projectPath = Join-Path $repositoryRoot 'SymlinkCreator.UI\SymlinkCreator.UI.csproj'
$wingetTemplateDirectory = Join-Path $repositoryRoot 'scripts\winget'
# APPINSTALLER_CLI_ERROR_MANIFEST_VALIDATION_WARNING
$wingetManifestValidationWarning = [int32]0x8A150028

[xml] $buildProps = Get-Content -LiteralPath $buildPropsPath -Raw
$projectVersion = @($buildProps.Project.PropertyGroup | Where-Object { $_.Version } | ForEach-Object { [string] $_.Version })[0]
if ([string]::IsNullOrWhiteSpace($projectVersion)) {
    throw "The project version could not be read from '$buildPropsPath'."
}

$releaseVersion = $projectVersion
$tag = "v$releaseVersion"
$null = & git -C $repositoryRoot rev-parse --verify --quiet "refs/tags/$tag^{commit}"
$tagLookupExitCode = $LASTEXITCODE
$archiveRevision = 'HEAD'
$archiveRevisionLabel = 'HEAD'
if ($tagLookupExitCode -eq 0) {
    $archiveRevision = $tag
    $archiveRevisionLabel = $tag
}
elseif ($tagLookupExitCode -ne 1) {
    throw "Unable to check whether local release tag '$tag' exists."
}

if ($Publish -and $tagLookupExitCode -eq 1) {
    $remoteTagLines = @(& git -C $repositoryRoot ls-remote --exit-code --tags origin "refs/tags/$tag" "refs/tags/$tag^{}")
    $remoteTagExitCode = $LASTEXITCODE
    if ($remoteTagExitCode -eq 0) {
        $remoteTagLine = $remoteTagLines | Where-Object { $_ -match '\^\{\}$' } | Select-Object -First 1
        if ([string]::IsNullOrWhiteSpace($remoteTagLine)) {
            $remoteTagLine = $remoteTagLines[0]
        }
        $remoteTagCommit = @($remoteTagLine -split '\s+')[0]
        if ($remoteTagCommit -notmatch '^[0-9a-fA-F]{40,64}$') {
            throw "Remote release tag '$tag' resolved to an invalid Git object '$remoteTagCommit'."
        }
        $archiveRevision = $remoteTagCommit
        $archiveRevisionLabel = $tag
    }
    elseif ($remoteTagExitCode -ne 2) {
        throw "Unable to check whether remote release tag '$tag' exists."
    }
}

$commitTimestampText = & git -C $repositoryRoot show -s --format=%ct $archiveRevision
if ($LASTEXITCODE -ne 0) {
    throw "Unable to read the $archiveRevisionLabel commit timestamp used for reproducible ZIP metadata."
}
$commitTimestampText = ([string] $commitTimestampText).Trim()
$commitTimestampSeconds = [long] 0
if (-not [long]::TryParse(
        $commitTimestampText,
        [System.Globalization.NumberStyles]::None,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref] $commitTimestampSeconds)) {
    throw "Git returned an invalid $archiveRevisionLabel commit timestamp '$commitTimestampText'."
}
$archiveTimestamp = [System.DateTimeOffset]::FromUnixTimeSeconds($commitTimestampSeconds).ToUniversalTime()
# ZIP stores seconds at two-second precision; normalize once so repeated builds have identical metadata.
$archiveTimestamp = $archiveTimestamp.AddSeconds(-($archiveTimestamp.Second % 2))
$minimumZipTimestamp = [System.DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [System.TimeSpan]::Zero)
$maximumZipTimestamp = [System.DateTimeOffset]::new(2107, 12, 31, 23, 59, 58, [System.TimeSpan]::Zero)
if ($archiveTimestamp -lt $minimumZipTimestamp -or $archiveTimestamp -gt $maximumZipTimestamp) {
    throw "The $archiveRevisionLabel commit timestamp '$archiveTimestamp' is outside the ZIP timestamp range."
}

if ($Publish -and -not [string]::IsNullOrWhiteSpace($env:GITHUB_REF_NAME) -and $env:GITHUB_REF_NAME -ne $tag) {
    throw "The workflow tag '$($env:GITHUB_REF_NAME)' does not match the project release tag '$tag'."
}

$defaultOutputDirectory = if ($LaunchForManualTest) {
    Join-Path $repositoryRoot 'artifacts\local-release'
}
else {
    Join-Path $repositoryRoot 'artifacts'
}
$outputDirectoryPath = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $defaultOutputDirectory
}
elseif ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputDirectory))
}

$stagingDirectory = Join-Path $outputDirectoryPath ".release-staging-$([guid]::NewGuid().ToString('N'))"
$manifestDirectory = Join-Path $stagingDirectory "winget\$releaseVersion"
$finalManifestDirectory = Join-Path $outputDirectoryPath "winget\$releaseVersion"
$installerManifestName = 'ArnobPaul.SymlinkCreator.installer.yaml'
$releaseTargets = @(
    [pscustomobject] @{
        Platform = 'x64'
        RuntimeIdentifier = 'win-x64'
        WinGetArchitecture = 'x64'
        AssetName = 'Symlink.Creator.x64.zip'
    },
    [pscustomobject] @{
        Platform = 'ARM64'
        RuntimeIdentifier = 'win-arm64'
        WinGetArchitecture = 'arm64'
        AssetName = 'Symlink.Creator.arm64.zip'
    }
)
$hostPlatform = switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
    ([System.Runtime.InteropServices.Architecture]::X64) { 'x64'; break }
    ([System.Runtime.InteropServices.Architecture]::Arm64) { 'ARM64'; break }
    default { $null }
}
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
    param([string] $ExecutablePath)

    $wingetCreate = if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
        $command = Get-Command wingetcreate -ErrorAction SilentlyContinue
        if ($null -eq $command) { $null } else { $command.Source }
    }
    else {
        [System.IO.Path]::GetFullPath($ExecutablePath)
    }

    if ([string]::IsNullOrWhiteSpace($wingetCreate) -or -not (Test-Path -LiteralPath $wingetCreate -PathType Leaf)) {
        throw 'wingetcreate was not found. Install it or pass -WinGetCreatePath.'
    }
    return $wingetCreate
}

function Write-WinGetManifestSet {
    param(
        [Parameter(Mandatory)] [string] $Directory,
        [Parameter(Mandatory)] [object[]] $Packages
    )

    $x64Package = @($Packages | Where-Object Platform -eq 'x64')
    $arm64Package = @($Packages | Where-Object Platform -eq 'ARM64')
    if ($x64Package.Count -ne 1 -or $arm64Package.Count -ne 1) {
        throw 'WinGet manifest generation requires exactly one x64 package and one ARM64 package.'
    }

    if (Test-Path -LiteralPath $Directory) {
        Remove-Item -LiteralPath $Directory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Directory -Force | Out-Null

    $replacements = @{
        '__VERSION__' = $releaseVersion
        '__X64_INSTALLER_URL__' = $x64Package[0].InstallerUrl
        '__X64_INSTALLER_SHA256__' = $x64Package[0].ZipSha256
        '__ARM64_INSTALLER_URL__' = $arm64Package[0].InstallerUrl
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
    $zipPath = Join-Path $stagingDirectory $Target.AssetName
    Write-Information "Publishing the $($Target.Platform) framework-dependent application..." -InformationAction Continue
    & dotnet publish $projectPath `
        --configuration Release `
        --runtime $Target.RuntimeIdentifier `
        -p:Platform=$($Target.Platform) `
        --output $publishDirectory | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish for $($Target.Platform) failed with exit code $LASTEXITCODE."
    }

    $publishEntries = @(Get-ChildItem -LiteralPath $publishDirectory -File -Recurse -Force | Sort-Object FullName)
    if ($publishEntries.Count -eq 0) {
        throw "The $($Target.Platform) publish directory did not contain any files."
    }
    $archiveNames = @(
        $publishEntries |
            ForEach-Object { [System.IO.Path]::GetRelativePath($publishDirectory, $_.FullName).Replace('\', '/') } |
            Sort-Object
    )
    foreach ($requiredName in @('SymlinkCreator.exe', 'resources.pri')) {
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
        Platform = $Target.Platform
        WinGetArchitecture = $Target.WinGetArchitecture
        AssetName = $Target.AssetName
        ZipPath = $zipPath
        ZipLength = (Get-Item -LiteralPath $zipPath).Length
        ZipSha256 = $zipHash
        ArchiveNames = $archiveNames
        InstallerUrl = "https://github.com/$Repository/releases/download/$tag/$($Target.AssetName)"
    }
}

function Invoke-ReleasePackaging {
    New-Item -ItemType Directory -Path $outputDirectoryPath -Force | Out-Null
    New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

    Write-Information "Packaging Symlink Creator $releaseVersion for x64 and ARM64..." -InformationAction Continue
    Write-Information "Using Git $archiveRevisionLabel timestamp $($archiveTimestamp.ToString('u')) for reproducible ZIP entries." -InformationAction Continue
    $validationPlatform = if ($null -eq $hostPlatform) { 'x64' } else { $hostPlatform }
    Write-Information "Validating source and running tests for the host-compatible $validationPlatform target..." -InformationAction Continue
    & $buildScriptPath -TargetPlatform $validationPlatform -Configuration Release -Verify | Out-Host

    $packages = @($releaseTargets | ForEach-Object { Invoke-ArchitecturePackaging -Target $_ })
    Write-WinGetManifestSet -Directory $manifestDirectory -Packages $packages
    Write-Information "Generated WinGet manifests: $manifestDirectory" -InformationAction Continue

    return [pscustomobject] @{
        Packages = $packages
        ManifestDirectory = $manifestDirectory
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
        $fileVersion = (Get-Item -LiteralPath $executablePath).VersionInfo.FileVersion
        if ($fileVersion -notmatch "^$escapedVersion(?:\.0)?$") {
            throw "$($Package.Platform) EXE FileVersion '$fileVersion' does not match project version '$releaseVersion'."
        }

        Write-Information "$($Package.Platform) release ZIP and EXE version verified." -InformationAction Continue
        if ($shouldLaunch) {
            Write-Information "Fresh $($Package.Platform) extraction retained for manual testing: $extractRoot" -InformationAction Continue
            Start-Process -FilePath $executablePath -WorkingDirectory $extractRoot | Out-Null
            Write-Information "Launched the host-compatible $($Package.Platform) release." -InformationAction Continue
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
        if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
            throw 'winget is required to validate the generated local manifest.'
        }
        & winget validate --manifest $Release.ManifestDirectory
        $validationExitCode = $LASTEXITCODE
        if ($validationExitCode -eq $wingetManifestValidationWarning) {
            Write-Information 'WinGet manifest validation succeeded with warnings.' -InformationAction Continue
        }
        elseif ($validationExitCode -ne 0) {
            throw "WinGet manifest validation failed with exit code $validationExitCode."
        }
        $global:LASTEXITCODE = 0
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

    $response = @(& gh api "repos/$Repository/releases/tags/$ReleaseTag" --jq `
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

function Assert-GitHubReleaseAsset {
    param(
        [Parameter(Mandatory)] [object] $GitHubRelease,
        [Parameter(Mandatory)] [pscustomobject] $Package
    )

    if ($GitHubRelease.tagName -ne $tag) {
        throw "GitHub release verification returned tag '$($GitHubRelease.tagName)' instead of '$tag'."
    }
    $asset = @($GitHubRelease.assets | Where-Object name -eq $Package.AssetName)
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
            '--repo', $Repository,
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
    $status = @(git status --porcelain --untracked-files=all)
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

    Assert-CleanReleaseSource
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw 'GitHub CLI (gh) is required to create the GitHub release.'
    }
    $headCommit = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($headCommit)) {
        throw 'Unable to resolve the current Git commit for the GitHub release tag.'
    }

    $githubRelease = Get-GitHubRelease -ReleaseTag $tag
    if ($null -eq $githubRelease) {
        Write-Information "Creating GitHub release $tag..." -InformationAction Continue
        $createArguments = @('release', 'create', $tag)
        $createArguments += @($Release.Packages | ForEach-Object ZipPath)
        $createArguments += @(
            '--repo', $Repository,
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
        $githubRelease = Get-GitHubRelease -ReleaseTag $tag
        if ($null -eq $githubRelease) {
            throw "GitHub release '$tag' could not be found after creation."
        }
    }
    else {
        Write-Information "GitHub release $tag already exists; verifying it so publication can resume safely." -InformationAction Continue
        foreach ($package in $Release.Packages) {
            if (@($githubRelease.assets | Where-Object name -eq $package.AssetName).Count -eq 0) {
                Write-Information "Uploading missing asset $($package.AssetName)..." -InformationAction Continue
                Invoke-CheckedCommand -FilePath 'gh' -ArgumentList @(
                    'release', 'upload', $tag, $package.ZipPath,
                    '--repo', $Repository
                )
                $githubRelease = Get-GitHubRelease -ReleaseTag $tag
                if ($null -eq $githubRelease) {
                    throw "GitHub release '$tag' could not be found after uploading $($package.AssetName)."
                }
            }
        }
        if (-not [string]::IsNullOrWhiteSpace($NotesPath)) {
            Invoke-CheckedCommand -FilePath 'gh' -ArgumentList @(
                'release', 'edit', $tag,
                '--repo', $Repository,
                '--notes-file', $NotesPath
            )
            Write-Information "GitHub release notes updated from '$NotesPath'." -InformationAction Continue
        }
    }

    foreach ($package in $Release.Packages) {
        Assert-GitHubReleaseAsset -GitHubRelease $githubRelease -Package $package
    }
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
        $wingetCreate = Resolve-WinGetCreate -ExecutablePath $WinGetCreatePath
    }

    $release = Invoke-ReleasePackaging
    Test-ReleaseBundle -Release $release
    Complete-ReleaseBundle -Release $release

    if ($Publish) {
        if ($PSCmdlet.ShouldProcess(
                "$Repository and microsoft/winget-pkgs",
                "publish GitHub release $tag and submit its WinGet manifest")) {
            Publish-Release -Release $release -WinGetCreate $wingetCreate -NotesPath $resolvedReleaseNotesPath
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
    Pop-Location
}
