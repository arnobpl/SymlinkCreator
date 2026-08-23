# Symlink Creator

[![CI](https://github.com/arnobpl/SymlinkCreator/actions/workflows/ci.yml/badge.svg)](https://github.com/arnobpl/SymlinkCreator/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Release](https://img.shields.io/github/v/release/arnobpl/SymlinkCreator)](https://github.com/arnobpl/SymlinkCreator/releases/latest)

Symlink Creator is a Windows GUI app for creating multiple symbolic links (symlinks) at once using the [`mklink`](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/mklink) command.

## Get Symlink Creator

[![Install with WinGet](https://img.shields.io/badge/Install-WinGet-blue?style=for-the-badge&logo=windows)](https://github.com/microsoft/winget-pkgs/tree/master/manifests/a/ArnobPaul/SymlinkCreator)
[![Download ZIP](https://img.shields.io/badge/Download-ZIP-blue?style=for-the-badge&logo=github)](https://github.com/arnobpl/SymlinkCreator/releases/latest)

### Recommended: Install via WinGet

```powershell
winget install --id ArnobPaul.SymlinkCreator --exact
```

WinGet installs Symlink Creator with its required dependencies and adds a command alias:

```powershell
symlinkcreator
```

### Manual download

📦 [Download for x64](https://github.com/arnobpl/SymlinkCreator/releases/latest/download/Symlink.Creator.x64.zip)

📦 [Download for ARM64](https://github.com/arnobpl/SymlinkCreator/releases/latest/download/Symlink.Creator.arm64.zip)

🗂️ [View all releases](https://github.com/arnobpl/SymlinkCreator/releases)

Before running the framework-dependent ZIP, install its prerequisites:

```powershell
winget source update
winget install --id Microsoft.DotNet.Runtime.10 --exact --source winget
winget install --id Microsoft.WindowsAppRuntime.2 --exact --source winget
$vcArchitecture = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'arm64' } else { 'x64' }
$vcRuntime = "Microsoft.VCRedist.2015+.$vcArchitecture"
winget install --id $vcRuntime --exact --source winget
```

## Development

Build, test, clean, and release instructions are available in the [development and release guide](docs/Development.md).

## Use cases

- Suppose, you have a collection of several songs sorted by artists and albums on your PC. You might want a separate collection of your favorite songs which you will store on your mobile devices. In this scenario, the traditional shortcut option through the File Explorer right-click context menu is insufficient, because you cannot copy the actual file contents by copying the traditional shortcut files (_\*.lnk_). You might consider duplicating the files which you will store on your mobile devices. But it will waste the storage space of your PC. In this case, Symlink Creator will come in handy. You can easily create a separate collection of songs and transfer them to your mobile devices, without wasting your PC's storage space.

- Suppose, you have a special folder that is linked to your online storage like Google Drive. You might want some specific files/folders to be backed up from other folders. A traditional shortcut file is not helpful here to back up those files. In this scenario, you can use Symlink Creator for backup purposes without duplicating those files/folders in the special folder.

- Suppose, you play video games a lot and you have the Steam client to manage those games. You have set a non-system drive (say, _D:_) to download the games. But that non-system drive has slow read capacity but your system drive (say, _C:_) has SSD which is a lot faster to read. In that scenario, you can use Symlink Creator to save your favorite video games in the SSD so that you can load those games faster without changing any settings in the Steam client. Symlink Creator can create symlinks of the folders of video games in the slow non-system drive, but the game files are actually stored in the fast SSD.

## What Symlink Creator does

Symlink Creator creates _symlinks_ which is an NTFS feature. Unlike the traditional shortcut files (_\*.lnk_), symlinks do not have any _file size_. While symlinks may be called advanced shortcut files, they appear to be real files. Unlike duplicated files, symlinks do not waste your storage space. Symlink Creator works for both files and folders.

## How Symlink Creator works

- Symlink Creator uses the `mklink` command to create symlinks by generating and executing a script.
- It works on Windows 11/10.

## How to use Symlink Creator

![Screenshot](docs/assets/Screenshot.png "Screenshot of Symlink Creator")

- At the `Source file or folder list`, you can add files or folders which will be copied in the `Destination path` as symlinks.
- Using Symlink Creator's drag-n-drop feature, you can easily create multiple symlinks at a time.
  - You can drag-n-drop files/folders directly from File Explorer.
  - You can also drag-n-drop the text containing a list of file/folder paths separated by a new line such as:
  ```
  D:\TestingSymlinkCreator/Src/MyFile1.txt
  D:\TestingSymlinkCreator/Src/MyFile2.txt
  ```
- Tick the `Use relative path if possible` option to use relative paths while creating symlinks. In this case, relative paths will be used if both source files/folders and destination files/folders are in the same drive.
- Tick the `Retain script file on Desktop after execution` option to keep the generated command script for logging or other advanced use.
- Tick the `Hide successful operation dialog` option if you want to only show a dialog when an error occurs.

### Shortcut options

You can append these startup options to a Windows shortcut's target to configure how Symlink Creator behaves when it starts:

- `--no-elevation-warning` prevents the warning about drag-and-drop potentially not working when Symlink Creator is launched as administrator. It does not disable the administrator prompt required to create symlinks.
- `--language <tag>` selects the app language for that launch. Use one of the locale-folder names under [`SymlinkCreator.UI/Strings`](SymlinkCreator.UI/Strings), such as `zh-CN`, `ja-JP`, `pt-BR`, or `ko-KR`. Windows language settings are unchanged.
- `--absolute-paths` makes each symlink point to the source using its complete path, even when a shorter relative path could be used. This keeps the link target independent of the destination folder's location.
- `--retain-script` keeps the generated `.cmd` file on the Desktop after symlink creation. The file contains the Windows commands used by Symlink Creator and can be reviewed for troubleshooting or advanced use.
- `--hide-success-dialog` skips the confirmation dialog after symlink creation succeeds. Error and canceled-operation dialogs are still shown.

For a WinGet installation, first find the command alias path in PowerShell:

```powershell
where.exe symlinkcreator
```

Copy the path that this command returns into the shortcut target, followed by the desired options. For example:

```text
"C:\Users\<username>\AppData\Local\Microsoft\WinGet\Links\symlinkcreator.exe" --language zh-CN --absolute-paths --retain-script --hide-success-dialog
```

The three preference options apply only to that launch and can still be changed using the checkboxes. Language selection and elevation-warning suppression are startup-only options.

## Why Symlink Creator needs administrative rights

It has been stated before that Symlink Creator uses the `mklink` command to create symlinks. The `mklink` command requires administrative privilege to create symlinks. You can find more information [here](https://learn.microsoft.com/en-us/windows/security/threat-protection/security-policy-settings/create-symbolic-links).

## Support Symlink Creator

Symlink Creator is a simple tool, but if it has saved you time or made things a bit easier, consider supporting the project. Every contribution helps keep it going and encourages future improvements.

<a href='https://ko-fi.com/O4O01L2D7P' target='_blank'><img height='36' style='border:0px;height:36px;' src='https://storage.ko-fi.com/cdn/kofi6.png?v=6' border='0' alt='Buy Me a Coffee at ko-fi.com' /></a>

You can donate via [PayPal](https://paypal.me/arnobpl).

<img src="docs/assets/qr-paypal.jpg" alt="PayPal QR Code" width="200">

You can also send crypto tokens to the following addresses:

<table>
  <thead>
    <tr>
      <th>Blockchain</th>
      <th>QR Code and Address</th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td>Ethereum</td>
      <td>
        <img src="docs/assets/QR-Ethereum.png" alt="Ethereum QR Code" width="200"><br>
        <code>0x2536B9A9a6b49234db2006482f43d02BEE6FDd07</code>
      </td>
    </tr>
    <tr>
      <td>Bitcoin</td>
      <td>
        <img src="docs/assets/QR-Bitcoin.png" alt="Bitcoin QR Code" width="200"><br>
        <code>bc1qwhwqal63y629ltnyhvr0txl5xngnhh9dv9u5yf</code>
      </td>
    </tr>
  </tbody>
</table>

If donating is not an option, simply starring the repo, sharing feedback, or spreading the word is equally appreciated. Thank you for using Symlink Creator and sharing your thoughts.

Happy Symlinking!
