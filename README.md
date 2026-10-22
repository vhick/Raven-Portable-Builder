# Raven Portable Builder v1.1

**Official Raven by mjishnu:** https://github.com/mjishnu/Raven  
**Custom Windows launcher:** standalone ZIP, not affiliated with Raven's developers.

This manually triggered GitHub Actions workflow downloads the **official stable Windows x64 self-contained Raven release**, extracts its original program files and high-resolution icon, creates `RavenPortable.exe`, and distributes one Windows portable ZIP with an `App`, `Data`, `Other` layout compatible with the PortableApps.com Menu.

## Easy GitHub build instructions

1. Create a NEW GitHub repository named `Raven-Portable-Builder`.
2. Extract this builder ZIP on your computer.
3. Upload every extracted file and folder to your new repository, **including `.github/workflows/build-portable.yml`**. Do not upload the ZIP itself as the only file.
4. Commit your changes to `main`.
5. In GitHub, click **Actions → Build Raven Portable (Manual) → Run workflow**.
6. Keep `upstream_tag=latest` to use the current stable release, or specify a stable tag such as `v1.1.1.0`.
7. After the build completes, download the **Raven-Portable-Windows-x64** artifact.
8. Open that artifact's ZIP, then extract `RavenPortable-v...-Windows-x64.zip` to a writable folder or portable drive. Open `RavenPortable\RavenPortable.exe`.

## Portable layout

```
RavenPortable/
├── RavenPortable.exe                 # Original-icon custom launcher
├── App/
│   ├── Raven/                         # Official self-contained Raven binaries
│   └── AppInfo/                       # PortableApps.com metadata and original icon
├── Data/
│   ├── LocalAppData/Raven/            # Preferences, Raven logs, downloads, download history
│   ├── Temp/                         # Temporary files for the Raven process
│   └── TraceGuard/                   # Local diagnostics and ownership marker
└── Other/
    ├── Manage-RavenJunction.ps1      # Creates/removes temporary Raven data junction
    └── ...
```

**The final app package is a ZIP, never a `.paf.exe` installer.** GitHub artifact retention is one day. Future builder runs fetch the latest published *stable* release by default.

## Why Raven requires a different portability technique

Raven's current source uses `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)` and writes under `%LOCALAPPDATA%\Raven` (including `ApplicationData\LocalSettings.json`, `Downloads.json`, and downloaded packages). This is a Windows Known Folder, and changing environment variables alone does not reliably redirect it.

To redirect Raven without altering the official EXE, the launcher temporarily creates a **directory junction** from `%LOCALAPPDATA%\Raven` to `Data\LocalAppData\Raven`. That makes Raven store its ordinary files on your portable drive. The launcher removes **only its own verified junction** after Raven has exited; your data stays under `Data`.

It never deletes an existing `%LOCALAPPDATA%\Raven` folder or someone else's junction. If installed Raven already uses that folder, portable Raven **refuses to start**, rather than accidentally mixing installed and portable profiles. A temporary junction must not be removed while Raven is running.

**Crash recovery:** If the portable launcher is interrupted, the junction might remain. On the next launch, if there is no other `Raven.exe` process, the launcher uses the recorded owner marker under `Data\TraceGuard` to verify and remove its own previous junction, then recreates a fresh one. Junction targets are not assumed to have fixed IDs or fixed drive letters. If ownership cannot be proven, the launcher does not modify the host folder.

## TraceGuard behavior

- Filesystem: before/after snapshots of Raven-specific Roaming and Local AppData folders, with safe cleanup of newly created **empty** non-reparse directories only. Data-bearing folders are preserved.
- Windows Registry: snapshots of `HKCU\Software\Raven`. New path-linked string values and newly created empty keys are cleaned cautiously, while existing keys/values are preserved. HKLM, file associations, Windows app registrations, and other shared changes are audit-only.
- Logs: `Data\TraceGuard\junction.log`, `last-run.log`, `registry.log`.
- A Raven instance found before launch blocks the operation. If an instance remains after the launched process exits, junction cleanup is postponed to protect active downloads.
- No admin privileges are requested. The junction requires a writable `%LOCALAPPDATA%` and Windows PowerShell; a restrictive Windows policy may prevent it.

**Critical limitation:** Raven is an app **installer and updater**. When you deliberately install/update a Microsoft Store app through Raven, it changes the Windows computer. Those app installation files, registry entries, packages, services, dependencies, and Windows caches are **not** portable Raven data and **must not be removed by TraceGuard**. Likewise Windows system-level traces of Raven (recent items, Prefetch, event logs) may remain. This is a safer portable *client*, not a Windows sandbox or a tool that reverses installations.

## Moving and upgrading

- Close Raven Portable before moving or replacing files.
- **Always preserve the existing `Data` folder.** Back it up before upgrades. Replace only `App`, `Other`, and `RavenPortable.exe` with versions from the newer package.
- After moving to a new drive, the launcher recreates its junction against the current `Data` location. Previously stored **absolute download paths** in Raven's JSON may still reference your old drive letter; reselect/retry affected downloads if needed.
- Avoid Raven's built-in self-update for the Raven client. Update the portable app by rerunning GitHub Actions and replacing only program files.
- When Raven Portable is open, don't simultaneously run another Raven installation. Wait until downloads and installs finish and exit normally.

## v1.1: Junction self-test repair (October 22, 2026)

The first v1.0 GitHub run downloaded/extracted Raven, extracted its icon, and compiled the launcher, but failed `--junction-self-test` (exit code 99). The test executable is compiled in `work/`, while its junction script was only in the repository's `Other/` folder. The workflow now **copies the real helper to `work/Other/` before testing**, matching the layout of the packaged app. The test also writes `work/junction-self-test.log` with its exact phase/error if it fails, and the workflow prints that diagnostic automatically. This does not modify the portable profile/data or weaken the conservative cleanup safeguards.

Use **a new GitHub Actions run after committing v1.1** (not Re-run jobs on the old commit).

## Validation

The builder contains portable-format and release-selection unit tests plus **four compiled Windows launcher self-tests**, including real temporary junction creation/removal and non-interference with an existing folder. The junction test creates only disposable paths under `%TEMP%` and does not install Raven.

## Licenses and security

Raven is an independent Apache-2.0 project by mjishnu. We download the developer's original binaries without rewriting them. The custom launcher and build scripts are MIT licensed. Original icon remains upstream artwork. No prebuilt `RavenPortable.exe` is distributed inside this builder ZIP; GitHub compiles the launcher with the official icon.

Use only Microsoft Store apps that you are entitled to download/install. Windows app-installation permissions still apply.
