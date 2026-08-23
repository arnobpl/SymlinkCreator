#Requires -Version 7.4

[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Low')]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$generatedDirectories = @(
    'SymlinkCreator.UI\bin',
    'SymlinkCreator.UI\obj',
    'SymlinkCreator.Application\bin',
    'SymlinkCreator.Application\obj',
    'SymlinkCreator.Tests\bin',
    'SymlinkCreator.Tests\obj',
    'SymlinkCreator.Tests\TestResults',
    'artifacts'
)

foreach ($relativePath in $generatedDirectories) {
    $directoryPath = Join-Path $repositoryRoot $relativePath
    if ((Test-Path -LiteralPath $directoryPath) -and
        $PSCmdlet.ShouldProcess($directoryPath, 'Remove generated directory')) {
        Write-Information "Removing $directoryPath" -InformationAction Continue
        Remove-Item -LiteralPath $directoryPath -Recurse -Force
    }
}

if ($WhatIfPreference) {
    Write-Information 'Cleanup preview complete; nothing was removed.' -InformationAction Continue
}
else {
    Write-Information 'Generated project outputs and release artifacts cleaned.' -InformationAction Continue
}
