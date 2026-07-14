# Avalonia 12.1 Upgrade Design

**Date:** 2026-07-12
**Project:** Downio
**Scope:** Upgrade the Avalonia ecosystem on `master` to the latest stable compatible releases without changing the application release version.

## Goals

- Upgrade the Avalonia application from 11.3.11 to 12.1.0.
- Upgrade Avalonia-coupled third-party packages to stable compatible versions.
- Make only compatibility changes that are demonstrated as necessary by restore, compilation, XAML loading, publishing, or runtime behavior.
- Preserve .NET 10, Native AOT, trimming, single-file publishing, and the existing six-RID CI matrix.
- Verify the upgraded application on the current macOS x64 development machine.

## Non-goals

- Do not change the Downio application version.
- Do not create a release, tag, or publish artifacts externally.
- Do not introduce central package management or unrelated refactoring.
- Do not merge unrelated product or UI changes from `dev`.
- Do not claim Windows or Linux runtime verification from the macOS environment.

## Tooling and Package Sources

All .NET operations will use the SDK selected by `DOTNET_ROOT`:

```bash
"$DOTNET_ROOT/dotnet" ...
```

At design time, this resolves to .NET SDK 10.0.300 at:

```text
/Users/percy/.local/share/mise/installs/dotnet/10.0.300
```

NuGet restore and package-update commands will use the user's enabled package sources, including the configured Huawei Cloud mirror. The implementation will not edit global NuGet source configuration. If an enabled mirror has not synchronized a required version, the implementation will report the exact package and version rather than silently changing sources or downgrading packages.

## Dependency Changes

Update the following direct package references in `src/Downio/Downio.csproj`:

| Package | Current | Target |
|---|---:|---:|
| Avalonia | 11.3.11 | 12.1.0 |
| Avalonia.Desktop | 11.3.11 | 12.1.0 |
| Avalonia.Themes.Fluent | 11.3.11 | 12.1.0 |
| Avalonia.Fonts.Inter | 11.3.11 | 12.1.0 |
| Avalonia.Controls.DataGrid | 11.3.11 | 12.1.0 |
| Avalonia.Controls.ColorPicker | 11.3.11 | 12.1.0 |
| LiveMarkdown.Avalonia | 1.7.0 | 2.2.0 |
| Avalonia.Diagnostics | 11.3.11 | Remove |

The Avalonia core, desktop, theme, font, DataGrid, and ColorPicker packages remain version-aligned at 12.1.0. `Avalonia.Diagnostics` is removed because no Avalonia 12 stable release is available for that package. An Avalonia 11 diagnostics assembly must not be mixed into the Avalonia 12 application.

Use `dotnet add package` and `dotnet remove package` rather than manually guessing dependency metadata. Confirm the resulting direct and transitive dependency graph after restore.

## Compatibility Migration Strategy

The migration will proceed from observed failures rather than speculative edits:

1. Update the package references.
2. Restore using the configured NuGet sources.
3. Build the solution in Debug.
4. Resolve compiler and XAML compiler errors with the smallest focused compatibility changes.
5. Start the application and resolve runtime XAML-loading or resource failures.
6. Build Release and publish Native AOT for the host RID.

The repository's `dev` branch contains an earlier Avalonia 12 migration and may be consulted as evidence. Only changes required for the current 12.1.0 upgrade will be reproduced; unrelated UI, ViewModel, release, and product changes will not be copied.

Known compatibility areas to examine include:

- `App.axaml.cs`: removal or replacement of Avalonia 11 validation-plugin manipulation if the API is no longer available.
- `App.axaml`: Fluent theme resource URIs, DataGrid and ColorPicker resources, LiveMarkdown styles, tray menus, native menus, and theme resource keys.
- `Program.cs`: desktop lifetime, platform detection, Inter font setup, and macOS platform options.
- `MainWindow.axaml` and code-behind: extended client area, experimental acrylic controls, template-part selectors, title bar behavior, and native platform handles.
- `ThemeAccentService`: Fluent resource keys whose names or ownership may have changed.
- Storage picker calls, custom drag behavior, visual-tree APIs, localization dictionaries, and tray icon resource loading.

If a compatibility issue is encountered, preserve behavior and public boundaries where possible. Do not bundle cleanup or architecture changes with the migration.

## Error Handling and Blocking Conditions

- A missing mirror package is reported with the package ID, requested version, source-related restore output, and the operation that failed.
- Package downgrade, dependency conflict, vulnerability, XAML compiler, trimming, ILVerify, and Native AOT diagnostics are preserved in verification results.
- Each source change must correspond to a concrete compile, XAML, publish, or runtime compatibility problem.
- If a required third-party package does not support Avalonia 12.1.0, stop and report the blocker. Do not silently downgrade Avalonia or remove user-facing functionality.
- Do not modify global NuGet settings to work around a failure.

## Verification

### Dependency verification

- Restore the solution successfully.
- Inspect direct and transitive packages.
- Confirm that Avalonia assemblies resolve consistently to 12.1.0.
- Check for downgrade, incompatibility, and vulnerability warnings.

### Build verification

- Build the complete solution in Debug.
- Build the complete solution in Release.
- Do not introduce new Avalonia migration warnings beyond the existing baseline.

### Native AOT verification

Publish the application for the host `osx-x64` RID in Release while preserving the project's existing Native AOT, trimming, and single-file settings. Inspect ILVerify, trimmer, and AOT diagnostics. A successful managed Debug build alone is insufficient.

### Runtime verification

Launch the application on macOS x64 and verify:

- startup completes without an unhandled exception or XAML load failure;
- the main window renders;
- Light, Dark, and System theme selection remains functional;
- DataGrid and ColorPicker render and respond;
- LiveMarkdown content renders in the update UI;
- tray and native menu behavior remains available;
- custom macOS title bar layout and window dragging remain functional;
- file and directory picker flows open successfully;
- normal application exit produces no observed unhandled exception.

Where a flow requires state that is not available locally, report it as unverified rather than infer success.

### Cross-platform boundary

Retain the existing CI definitions for Windows x64/arm64, macOS x64/arm64, and Linux x64/arm64. Local verification covers only macOS x64. The completion report must separate locally observed behavior from behavior left to CI or platform-specific testing.

## Completion Criteria

The upgrade is complete when:

- all target dependency versions are present and `Avalonia.Diagnostics` is absent;
- restore succeeds through the configured package sources;
- Debug and Release solution builds succeed;
- the host Native AOT publish succeeds without unresolved Avalonia compatibility diagnostics;
- the application launches and the listed macOS smoke checks pass, or any unavailable checks are explicitly documented;
- no application version, release metadata, tag, unrelated feature, or global NuGet configuration is changed;
- the final report states the exact verification evidence and remaining Windows/Linux risks.
