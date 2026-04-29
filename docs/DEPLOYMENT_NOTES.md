# Deployment Notes

## Recommended Packaging Strategy

For ShackStack on Windows, the recommended shipping path is:

1. `dotnet publish --self-contained true -r win-x64`
2. wrap the published folder with Inno Setup

For this project, Inno Setup is the default installer recommendation for most non-Store desktop releases.

This gives users:

- a normal installer `.exe`
- no requirement to install the .NET runtime separately
- a familiar Windows install experience
- a clean place to manage shortcuts, uninstall, and future updates
- easy scripting for custom install steps later, including drivers, registry keys, and migration tasks

## Why This Is The Default

This stack is a desktop radio application with:

- serial hardware access
- audio device access
- possible native dependencies later
- a need for predictable runtime behavior

A self-contained publish folder plus Inno Setup is the lowest-friction, lowest-surprise deployment target for that environment.

Inno Setup is a strong fit because it is:

- free
- widely used and trusted on Windows
- able to produce a single installer executable
- easy to script for hardware-adjacent install steps
- backed by a large ecosystem of examples and helpers

## Do Not Use Single-File Publish By Default

Single-file publish is not the preferred path here.

Reasons:

- native DLLs still extract at runtime
- antivirus tools often dislike temp extraction patterns
- startup behavior is less predictable
- cold start can be slower
- troubleshooting native dependency issues is harder

A self-contained folder wrapped in an installer gives most of the same user-facing simplicity without those downsides.

## MSIX Position

MSIX is not the default plan.

Only prefer MSIX if:

- ShackStack is being distributed through the Microsoft Store
- or a managed enterprise / MDM environment specifically benefits from it

For normal ham-radio distribution and field installs, Inno Setup is the more practical default.

## Native Dependency Policy

### Audio

Current preferred direction is `NAudio`.

Why this helps:

- it is managed code on Windows
- it avoids adding avoidable native packaging complexity early

### Serial / USB

The app should expect:

- many USB serial / CDC devices to work with inbox Windows support
- but FTDI / CP210x-class devices may still require correct driver presence

Packaging implication:

- document driver expectations clearly
- do not assume every system already has the right vendor driver

### Future Native Libraries

If later components require native DLLs:

- match the publish RID exactly
- publish `win-x64`
- ensure every bundled native binary is also `x64`

## Install Locations

Never write operational data beside the executable in `Program Files`.

Use:

- `AppData\Roaming` for user settings
- `AppData\Local` for logs, caches, transient data, and local working files

This avoids:

- UAC problems
- permission failures
- brittle portable-app assumptions

## Update Strategy

Preferred future update path:

- `Velopack`

Why:

- modern .NET desktop updater
- delta patch support
- background download support
- restart-and-swap behavior
- good fit for per-user installation

Avoid:

- `ClickOnce`

Reason:

- poor fit for this kind of hardware-aware desktop app
- awkward native dependency behavior

## Code Signing

Code signing should be treated as an early deployment requirement, not a late afterthought.

### Short version

- unsigned builds will trigger SmartScreen pain
- self-signed certificates do not solve real-user trust problems
- an EV cert is the cleanest long-term answer

### Why it matters

For a desktop ham-radio tool:

- users will download from the web
- installers will otherwise look suspicious to Windows
- update trust is much cleaner when the app is properly signed

## Baseline Publish Shape

Expected publish command shape:

```powershell
dotnet publish .\src\ShackStack.Desktop\ShackStack.Desktop.csproj -c Release --self-contained true -r win-x64
```

That publish output should then become the input to the installer project.

## ShackStack 1.0 Packaging Shape

For the `1.0` installer, build bundled decoder workers first, then publish the desktop app into the folder consumed by Inno Setup:

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\Build-DecoderWorkers.ps1
dotnet publish .\src\ShackStack.Desktop\ShackStack.Desktop.csproj -c Release -r win-x64 --self-contained true -o .\publish\ShackStack-win-x64-v1.0
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" ".\installer\ShackStack.iss"
```

Expected installer output:

```text
publish\ShackStack-Setup-v1.0.exe
```

The installer should package the built decoder workers from `src\ShackStack.Desktop\DecoderWorkers`; it should not rely on sidecars being present elsewhere on the operator machine.

## Recommended Later Additions

When the app matures, add:

- Inno Setup script under the repo
- publish helper script
- Velopack integration
- code-signing step in release pipeline

## Summary

The default ShackStack Windows deployment strategy should be:

- self-contained `win-x64` folder publish
- wrapped by Inno Setup
- settings in `AppData\Roaming`
- logs/cache in `AppData\Local`
- Velopack for updates later
- avoid single-file publish as the default
