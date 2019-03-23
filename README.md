# SymlinkCreator
SymlinkCreator is a GUI app for creating symbolic links (symlinks), and it is based on `mklink` command. You can create multiple symlinks at a time.

## Use cases
- Suppose, you have a collection of several songs sorted by artists and albums in your PC. You might want a separate collection of your most favorite songs which you will store in your mobile devices. In this scenario, the traditional shortcut option through Windows Explorer right-click context menu is not enough, because you cannot copy the actual file contents by copying the traditional shortcut files (*\*.lnk*). You might consider duplicating the files which you will store in your mobile devices. But it will waste your storage space of your PC. In this case, SymlinkCreator will come in handy. You can easily create the separate collection of songs and transfer them to your mobile devices, without wasting your PC's storage space.

- Suppose, you have a special folder which is linked to your online storage like Google Drive. You might want some specific files to be backed up from other folders. A traditional shortcut file is not helpful here to back up those files. In this scenario, you can use SymlinkCreator for the backup purpose without duplicating those files in the special folder.

## What SymlinkCreator does
SymlinkCreator creates *symlinks* which is an NTFS feature. Unlink the traditional shortcut files (*\*.lnk*), symlinks do not have any *file size*. While symlinks may be called advanced shortcut files, but they appear to be real files. Unlike duplicated files, symlinks do not waste your storage space. Currently, SymlinkCreator only works for files, not folders. I might add support for folders in the future.

## How SymlinkCreator works
SymlinkCreator uses `mklink` command to create symlinks. SymlinkCreator first creates a script file which contains `mklink` command lines, and execute it. SymlinkCreator works in Windows Vista, Windows 7 and Windows 10. It does not work in Windows XP because of the lack of `mklink` command.

## How to use SymlinkCreator
![Screenshot](SymlinkCreator/_ReadMe/Screenshot.png "Screenshot of SymlinkCreator")
- At the `Source file list`, you can add files which will be copied in `Destination path` as symlinks.
- Using SymlinkCreator's drag-n-drop feature, you can easily create multiple symlinks at a time.
- Tick `Use relative path if possible` option to use relative paths while creating symlinks. In this case, relative paths will be used if both source files and destination files are in the same drive.
- Tick `Retain script file after execution` option if you want to save the script file for later use like logging purpose.

## Why SymlinkCreator needs administrative rights
It has been stated before that SymlinkCreator uses `mklink` command to create symlinks. For some reasons, Microsoft decided symlink creation to be an administrative privilege. Later Microsoft has changed the decision and allowed Windows 10 Creators Update to create symlinks without requiring administrative rights. But for the compatibility reason, SymlinkCreator asks for administrative rights. SymlinkCreator may be improved later by checking specific Windows versions and by not asking for unnecessary administrative rights.
