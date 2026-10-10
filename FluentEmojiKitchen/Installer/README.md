# Build and distribute Fluent Emoji Kitchen

This folder packages the supplied app as an unpackaged, self-contained x64 Windows application inside one EXE installer. The app's source, UI, insertion, caches and hidden startup behavior are unchanged. No TSF registration is performed.

## Build on your Windows PC

1. Extract the complete ZIP to a writable folder. Use the same Visual Studio/.NET 10 SDK environment that builds your app. Friends do not need the SDK or Visual Studio.
2. Install Inno Setup 6.3 or later: https://jrsoftware.org/isdl.php . The compiler must be installed on your build PC only.
3. Exit any running development copy through its tray menu.
4. Double-click `Installer\Build-Installer.cmd`. It runs PowerShell without loading your profile and bypasses script execution policy for that process only. It does not change machine/user policy. Organization policy can still block it.
5. On success, distribute `Installer\output\FluentEmojiKitchen-Setup-1.0.0-x64.exe`. The ZIP is source code, not the finished installer.

The installer contains the entire published folder, including .NET and Windows App SDK runtimes. Keep the installed files together. The build checks the runtime payload before compiling the installer and writes a SHA-256 sidecar.

Command-line equivalents from the project directory:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Installer\build.ps1
# Next release (keep the installer AppId unchanged):
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Installer\build.ps1 -Version 1.0.1
# Optional compiler location:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Installer\build.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
# Only publish the runnable application folder:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Installer\build.ps1 -SkipInstaller
```

## Installation and startup

Installs for the current user under `%LOCALAPPDATA%\Programs\FluentEmojiKitchen`; no administrator elevation requested. Adds a Start menu shortcut and an Installed Apps uninstall entry. Desktop and Start with Windows checkboxes are optional and initially unchecked. Startup uses the current user's Startup folder and runs at sign-in. The existing app stays hidden until opened through its tray icon or hotkey. The final installer launch checkbox is also unchecked.

To enable startup later, rerun the installer and select Start with Windows, or place the installed app's shortcut in `shell:startup`. Uninstall removes the installer-owned shortcuts. Existing AppData caches and settings are preserved. Exit the app from its tray before updating/uninstalling. Avoid launching a second copy: this patch does not add single-instance behavior to the application.

## Test before sending to friends

Build and install on Windows, then test on a clean Windows 10 2004+ / Windows 11 x64 system without developer tools. This build targets x64 (ARM64 use requires Windows x64 emulation and separate verification). Confirm installation, tray-only launch, hotkey, catalogue insertion, kitchen downloads, image/URL copy, restart/sign-in with startup enabled, update over an older version, and uninstall. First-use downloads still need internet. A successful compile alone does not establish clean-machine runtime compatibility; report any missing-runtime message before distributing.

## Signing

This build produces an unsigned installer. Store submission or MSIX packaging is not required. Unsigned downloads can trigger Windows reputation warnings or be blocked by device policy. For trusted publisher identification, obtain a trusted Authenticode signing certificate/service and sign the application and installer; signing does not guarantee immediate SmartScreen reputation. A self-signed certificate is not automatically trusted on your friends' PCs. Never include a private signing key in source ZIPs. For a signed release, sign the published application before Inno compilation, configure Inno's SignTool/SignedUninstaller settings, sign the resulting installer, and regenerate its SHA-256 sidecar.

References: https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps and https://jrsoftware.org/ishelp/ .

## Changelog

- Added `Build-Installer.cmd`: double-click entry point with a process-scoped execution-policy setting and visible errors.
- Added `build.ps1`: Windows/tool checks, optional compiler path, version argument, clean x64 publish directory, self-contained .NET/Windows App SDK publishing, runtime payload checks, Inno compilation and checksum output.
- Disabled trimming for this publish command to avoid removing reflection-dependent code; retained normal multi-file app output inside one installer.
- Added `FluentEmojiKitchen.iss`: stable upgrade identity, current-user installation, app icon, Start menu entry, optional desktop/startup shortcuts, uninstall support, optional tray-only launch, removal of deselected optional shortcuts on upgrade.
- Added this build/distribution guide. No existing uploaded source files were edited.

Validation performed here: source preservation comparison, XML/archive integrity and packaging configuration review. Windows publish, Inno compilation and installed-app execution must be verified on Windows; they were not run in the Linux preparation environment.
