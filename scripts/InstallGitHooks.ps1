#Requires -Version 7.4

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hookPath = Join-Path $repositoryRoot '.githooks\pre-commit'
if (-not (Test-Path -LiteralPath $hookPath -PathType Leaf)) {
    throw "The pre-commit hook was not found at '$hookPath'."
}

git -C $repositoryRoot config --local core.hooksPath .githooks
if ($LASTEXITCODE -ne 0) {
    throw "Git hook configuration failed with exit code $LASTEXITCODE."
}

$configuredPath = git -C $repositoryRoot config --local --get core.hooksPath
if ($LASTEXITCODE -ne 0 -or $configuredPath -ne '.githooks') {
    throw "Git did not retain the expected repository-local hook path. Found: '$configuredPath'."
}

Write-Information 'Configured repository-local Git hooks from .githooks.' -InformationAction Continue
