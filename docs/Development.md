# Development and release

These instructions are for contributors and maintainers working on the project.

## First-time setup

Install PowerShell 7.4 or newer and the .NET 10 SDK on Windows. [`global.json`](../global.json) selects the supported SDK version.

```powershell
winget install --id Microsoft.PowerShell --exact --source winget
winget install --id Microsoft.DotNet.SDK.10 --exact --source winget
```

Install the PowerShell analyzer used by local verification:

```powershell
Set-PSRepository -Name PSGallery -InstallationPolicy Trusted
Install-Module PSScriptAnalyzer -Scope CurrentUser -Force
```

Optionally enable the repository's local Git hooks:

```powershell
.\scripts\InstallGitHooks.ps1
```

The hooks provide local pre-commit checks; the verification commands below remain the authoritative repository checks.

## Build and validate changes

Build the Release configuration for the current architecture:

```powershell
.\scripts\Build.ps1
```

Apply available PowerShell and C# formatting fixes:

```powershell
.\scripts\Build.ps1 -Fix
```

Run the full repository verification gate: formatting and style checks, script analysis, restore, build, and host-compatible tests:

```powershell
.\scripts\Build.ps1 -Verify
```

Preview generated-directory cleanup without changing anything:

```powershell
.\scripts\Clean.ps1 -WhatIf
```

Remove generated project outputs, test results, and release artifacts:

```powershell
.\scripts\Clean.ps1
```

Run cleanup after closing the application and stopping any local ZIP server.

## Test a packaged application

The release ZIP is framework-dependent. Before launching one directly, install the .NET runtime, Windows App Runtime, and Visual C++ runtime described in the [manual ZIP prerequisites](../README.md#manual-zip-prerequisites).

Build and verify both architecture ZIPs, then freshly extract and launch the package matching the current computer:

```powershell
.\scripts\Release.ps1 -LaunchForManualTest
```

This does not create a GitHub release or submit a WinGet PR. It also retains the fresh extraction for manual inspection. Test the normal unelevated launch, then start the same fresh extraction as administrator once and confirm that the drag-and-drop warning appears. Close the application before cleanup.

To build and validate the ZIPs and manifests without launching the application:

```powershell
.\scripts\Release.ps1
```

## Test local WinGet installation

This optional maintainer workflow is an end-to-end WinGet test. It requires WinGet and Python 3 (`py` or `python` on `PATH`), packages the release into `artifacts`, serves the ZIPs on loopback, prepares a local manifest, validates both URLs, and runs `winget validate`.

Confirm that Python is available before starting (`python --version` also works if the `py` launcher is unavailable):

```powershell
py --version
```

```powershell
.\scripts\Test-LocalWinGetManifest.ps1
```

The script prints two copy-pastable WinGet commands with the exact manifest path. Leave the script running in the current terminal, then copy those commands into a second, elevated PowerShell window. They have this form; use the actual path printed by the script instead of the placeholder:

```powershell
winget settings --enable LocalManifestFiles
winget install --manifest "<exact manifest path printed by the script>" --accept-source-agreements --accept-package-agreements
```

After the installation test, return to the first terminal and press Ctrl+C. The test script will stop its local ZIP server automatically.

After installation (or if the test fails), disable the temporary local-manifest setting in the second terminal:

```powershell
winget settings --disable LocalManifestFiles
```

If port `8765` is already in use, choose another loopback port:

```powershell
.\scripts\Test-LocalWinGetManifest.ps1 -Port 8876
```

If WinGet is unavailable, use `Release.ps1` for package/archive validation; the local WinGet integration test cannot run without WinGet.

## Version a release

Inspect the current version:

```powershell
Select-String -Path .\Directory.Build.props -Pattern '<Version>'
```

Change only the `<Version>` value in [`Directory.Build.props`](../Directory.Build.props). That value supplies the application and assembly metadata, GitHub release tag, ZIP URLs, WinGet `PackageVersion`, and WinGet PR title.

## Publish a release

Publication requires a clean worktree, GitHub CLI authentication, and `wingetcreate` available on `PATH`:

```powershell
gh auth login
wingetcreate token -s
```

After committing and pushing the release changes, publish with an optional Markdown release-notes file:

```powershell
.\scripts\Release.ps1 `
    -Publish `
    -ReleaseNotesPath "$env:USERPROFILE\Downloads\release.md"
```

Without `-ReleaseNotesPath`, the script passes GitHub CLI the fixed fallback text `Portable x64 and ARM64 releases of Symlink Creator <version>.`; it does not generate notes from commits. Use `-ReleaseNotesPath` when you want custom or LLM-generated Markdown release notes. To preview the external publication step while still running local validation:

```powershell
.\scripts\Release.ps1 `
    -Publish `
    -WhatIf `
    -ReleaseNotesPath "$env:USERPROFILE\Downloads\release.md"
```

`-SkipWinGetValidation` is only for non-publishing package/archive checks and cannot be combined with `-Publish`. `-ReplaceExistingRelease` is an advanced recovery option for intentionally replacing an existing GitHub release.

If WinGet submission fails after the GitHub release is created, rerun the same publish command. The script verifies the existing release and resumes the WinGet submission instead of creating a duplicate release.

Do not use `-Publish` during normal development. GitHub Actions runs the lint, build, test, and local release-package verification; it does not publish releases or submit WinGet PRs.
