# Development and release

These instructions are for contributors and maintainers working on the project.

## Build from source

Install PowerShell 7.4 or newer and the .NET 10 SDK on Windows. [`global.json`](../global.json) selects the supported SDK version.

```powershell
winget install --id Microsoft.PowerShell --exact --source winget
winget install --id Microsoft.DotNet.SDK.10 --exact --source winget
```

Build the Release configuration for the current architecture:

```powershell
.\scripts\Build.ps1
```

Run formatting, tests, and the build verification gate:

```powershell
.\scripts\Build.ps1 -Verify
```

Remove generated project outputs and release artifacts:

```powershell
.\scripts\Clean.ps1
```

Preview the exact directories first with `Clean.ps1 -WhatIf`.

## Release workflow

1. Update `<Version>` and make the code changes.
2. Run `Release.ps1 -LaunchForManualTest` and manually test the freshly extracted app.
3. Close the app, then run `Clean.ps1` to remove repository outputs and the retained manual-test folder under `artifacts`.
4. Commit and push the changes. GitHub publication requires a clean worktree.
5. Run `Release.ps1 -Publish` to create the GitHub release, upload both architecture ZIPs, and submit the WinGet PR.

## Version

Change only `<Version>` in [`Directory.Build.props`](../Directory.Build.props), for example:

```xml
<Version>2.0.1</Version>
```

The build uses that value for the application UI version, assembly/file/product metadata, GitHub release tag, ZIP URLs, WinGet `PackageVersion`, and WinGet PR title.

## Test locally

The release is framework-dependent. It needs one .NET runtime, the Windows App Runtime for WinUI 3, and the Microsoft Visual C++ runtime. Install these dependencies before launching a ZIP directly:

```powershell
winget source update
winget install --id Microsoft.DotNet.Runtime.10 --exact --source winget
winget install --id Microsoft.WindowsAppRuntime.2 --exact --source winget
$vcArchitecture = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'arm64' } else { 'x64' }
$vcRuntime = "Microsoft.VCRedist.2015+.$vcArchitecture"
winget install --id $vcRuntime --exact --source winget
```

Create and validate both architecture ZIPs, then freshly extract and launch the package matching the current computer without creating a GitHub release or WinGet PR:

```powershell
.\scripts\Release.ps1 -LaunchForManualTest
```

The script also generates the local WinGet manifests and runs `winget validate` through the packaging flow. Close the test application before cleanup.

During manual testing, also start the freshly extracted app as administrator once and confirm that the drag-and-drop warning appears; normal testing should remain unelevated.

To create and validate both ZIPs and the local WinGet manifests without launching the app or publishing anything:

```powershell
.\scripts\Release.ps1
```

## Publish

Authenticate GitHub CLI and WingetCreate once:

```powershell
gh auth login
wingetcreate token -s
```

Then publish locally. The command first validates the package, creates the GitHub release, uploads the ZIPs, and submits the WinGet PR:

```powershell
.\scripts\Release.ps1 -Publish -ReleaseNotesPath "$env:USERPROFILE\Downloads\release.md"
```

`ReleaseNotesPath` is optional. When supplied, the Markdown file becomes the GitHub release description. On a resumed publication, the script reapplies it to the existing release before submitting the WinGet PR. Without it, a short generated description is used.

Use `Release.ps1 -Publish -WhatIf` to build and validate the release while previewing the external publication step.

Before submitting, confirm that the WinGet source provides `Microsoft.WindowsAppRuntime.2`. `winget validate` checks the manifest structure but reports package dependencies as unvalidated.

If WinGet submission fails after the GitHub release is created, rerun the same command. The script verifies the existing release ZIP and resumes the WinGet submission instead of creating a duplicate release.

Do not pass `-Publish` during normal development. GitHub Actions runs the same lint, build, test, and local release-package verification as `Release.ps1`; it does not publish releases or submit WinGet PRs.
