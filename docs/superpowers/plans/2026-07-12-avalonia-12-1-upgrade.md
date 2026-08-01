# Avalonia 12.1 Upgrade Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade Downio from Avalonia 11.3.11 to Avalonia 12.1.0, update its Avalonia-coupled packages, apply only required compatibility migrations, and verify the result on macOS x64 without changing the application version.

**Architecture:** Keep the existing project structure and update only the package boundary and the two Avalonia 12 compatibility sites already evidenced by the repository's prior migration. Treat restore, XAML compilation, Debug/Release builds, Native AOT publish, and an observed macOS UI smoke test as executable compatibility checks; investigate any new failure before making further source changes.

**Tech Stack:** .NET SDK 10.0.300 via `"$DOTNET_ROOT/dotnet"`, Avalonia 12.1.0, LiveMarkdown.Avalonia 2.2.0, NuGet via the configured Huawei Cloud mirror, macOS x64, Native AOT.

---

## File Structure

- Modify `src/Downio/Downio.csproj`: align all Avalonia packages at 12.1.0, update LiveMarkdown.Avalonia to 2.2.0, and remove Avalonia.Diagnostics while preserving application version 1.0.55 and all existing AOT settings.
- Modify `src/Downio/App.axaml.cs`: remove the Avalonia 11 validation-plugin manipulation that is unavailable in Avalonia 12.
- Modify `src/Downio/App.axaml`: remove the obsolete dialog `ExtendClientAreaChromeHints` setter if the Avalonia 12 XAML compiler rejects it.
- Do not create a test project for this dependency migration. The repository has no test harness, and the affected behavior is framework/XAML/platform integration; the plan uses build, publish, and observed runtime checks instead.
- Do not modify `VERSION`, workflows, packaging scripts, application feature code, or global NuGet configuration.

### Task 1: Establish the Baseline and Update Dependencies

**Files:**
- Modify: `src/Downio/Downio.csproj:86-102`
- Verify unchanged: `src/Downio/Downio.csproj:13-14`

- [ ] **Step 1: Confirm the selected SDK, mirror, host RID, and starting application version**

Run:

```bash
printf 'DOTNET_ROOT=%s\n' "$DOTNET_ROOT"
"$DOTNET_ROOT/dotnet" --version
"$DOTNET_ROOT/dotnet" --info | grep '^ RID:'
"$DOTNET_ROOT/dotnet" nuget list source
python3 - <<'PY'
from pathlib import Path
text = Path('src/Downio/Downio.csproj').read_text()
assert '<Version>1.0.55</Version>' in text
assert 'Version="11.3.11"' in text
print('baseline package and application versions confirmed')
PY
```

Expected:

```text
DOTNET_ROOT=/Users/percy/.local/share/mise/installs/dotnet/10.0.300
10.0.300
 RID:         osx-x64
...
huaweicloud [Enabled]
...
baseline package and application versions confirmed
```

If the SDK, RID, or enabled mirror differs, stop and report the actual output rather than editing global configuration.

- [ ] **Step 2: Restore and build the Avalonia 11 baseline through the mirror**

Run:

```bash
NUGET_MIRROR='https://repo.huaweicloud.com/repository/nuget/v3/index.json'
"$DOTNET_ROOT/dotnet" restore Downio.sln --source "$NUGET_MIRROR"
"$DOTNET_ROOT/dotnet" build Downio.sln -c Debug --no-restore
```

Expected: restore succeeds and the Debug build ends with `Build succeeded.` Record the baseline warning count so the upgraded build can be compared against it.

- [ ] **Step 3: Remove Avalonia.Diagnostics using the .NET CLI**

Run:

```bash
"$DOTNET_ROOT/dotnet" remove src/Downio/Downio.csproj package Avalonia.Diagnostics
```

Expected: the complete `Avalonia.Diagnostics` `PackageReference`, including its `IncludeAssets` and `PrivateAssets` children, is removed.

- [ ] **Step 4: Update the Avalonia and LiveMarkdown references using the .NET CLI**

Run:

```bash
PROJECT='src/Downio/Downio.csproj'
DOTNET="$DOTNET_ROOT/dotnet"

"$DOTNET" add "$PROJECT" package Avalonia --version 12.1.0 --no-restore
"$DOTNET" add "$PROJECT" package Avalonia.Desktop --version 12.1.0 --no-restore
"$DOTNET" add "$PROJECT" package Avalonia.Themes.Fluent --version 12.1.0 --no-restore
"$DOTNET" add "$PROJECT" package Avalonia.Fonts.Inter --version 12.1.0 --no-restore
"$DOTNET" add "$PROJECT" package Avalonia.Controls.DataGrid --version 12.1.0 --no-restore
"$DOTNET" add "$PROJECT" package Avalonia.Controls.ColorPicker --version 12.1.0 --no-restore
"$DOTNET" add "$PROJECT" package LiveMarkdown.Avalonia --version 2.2.0 --no-restore
```

Expected: each command reports that the `PackageReference` was updated without performing restore.

- [ ] **Step 5: Inspect the exact project-file result before restore**

The package section in `src/Downio/Downio.csproj` must be:

```xml
    <ItemGroup>
        <PackageReference Include="Avalonia" Version="12.1.0" />
        <PackageReference Include="Avalonia.Desktop" Version="12.1.0" />
        <PackageReference Include="Avalonia.Themes.Fluent" Version="12.1.0" />
        <PackageReference Include="Avalonia.Fonts.Inter" Version="12.1.0" />
        <PackageReference Include="Avalonia.Controls.DataGrid" Version="12.1.0" />
        <PackageReference Include="Avalonia.Controls.ColorPicker" Version="12.1.0" />
        <PackageReference Include="LiveMarkdown.Avalonia" Version="2.2.0" />
        <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
        <!-- 仅在 Windows 编译时引入通知库 -->
        <PackageReference Include="CommunityToolkit.WinUI.Notifications" Version="7.1.2" Condition="$([MSBuild]::IsOSPlatform('Windows'))" />
    </ItemGroup>
```

Run:

```bash
python3 - <<'PY'
from pathlib import Path
text = Path('src/Downio/Downio.csproj').read_text()
for package in (
    'Avalonia',
    'Avalonia.Desktop',
    'Avalonia.Themes.Fluent',
    'Avalonia.Fonts.Inter',
    'Avalonia.Controls.DataGrid',
    'Avalonia.Controls.ColorPicker',
):
    assert f'Include="{package}" Version="12.1.0"' in text, package
assert 'Include="LiveMarkdown.Avalonia" Version="2.2.0"' in text
assert 'Avalonia.Diagnostics' not in text
assert '<Version>1.0.55</Version>' in text
print('target dependency set confirmed; application version unchanged')
PY
```

Expected: `target dependency set confirmed; application version unchanged`.

- [ ] **Step 6: Restore only through the configured mirror**

Run:

```bash
NUGET_MIRROR='https://repo.huaweicloud.com/repository/nuget/v3/index.json'
"$DOTNET_ROOT/dotnet" restore Downio.sln --source "$NUGET_MIRROR"
```

Expected: restore succeeds without `NU1101`, `NU1603`, `NU1605`, or package-source errors. If the mirror lacks a package, stop and report the exact package/version and restore diagnostic; do not enable or edit another source.

- [ ] **Step 7: Verify the resolved dependency graph and vulnerability result**

Run:

```bash
"$DOTNET_ROOT/dotnet" list src/Downio/Downio.csproj package --include-transitive
"$DOTNET_ROOT/dotnet" list src/Downio/Downio.csproj package --vulnerable --include-transitive
```

Expected:

- direct references show all six Avalonia packages at 12.1.0;
- LiveMarkdown.Avalonia is 2.2.0;
- Avalonia.Diagnostics is absent;
- no package downgrade is reported;
- the vulnerability command reports no vulnerable packages, or any unrelated pre-existing result is recorded without broadening this task.

### Task 2: Apply the Minimal Avalonia 12 Source Migration

**Files:**
- Modify: `src/Downio/App.axaml.cs:1-10,37-40,108-120`
- Modify only if confirmed by XAML compilation: `src/Downio/App.axaml:20-30`

- [ ] **Step 1: Run the upgraded Debug build as the failing compatibility test**

Run:

```bash
"$DOTNET_ROOT/dotnet" build Downio.sln -c Debug --no-restore
```

Expected before migration: failure associated with the Avalonia 11 validation-plugin API and/or the dialog chrome XAML setter. Preserve the exact compiler and XAML diagnostics. If the failure is different, invoke `superpowers:systematic-debugging` and investigate that failure before editing any additional file.

- [ ] **Step 2: Remove the Avalonia 11 validation-plugin manipulation**

In `src/Downio/App.axaml.cs`, remove these imports:

```csharp
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
```

Remove this call and its explanatory comment from `OnFrameworkInitializationCompleted`:

```csharp
// Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
// More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
DisableAvaloniaDataAnnotationValidation();
```

Remove the complete helper method:

```csharp
[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reflection-based validation is optional")]
private void DisableAvaloniaDataAnnotationValidation()
{
    // Get an array of plugins to remove
    var dataValidationPluginsToRemove =
        BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

    // remove each entry found
    foreach (var plugin in dataValidationPluginsToRemove)
    {
        BindingPlugins.DataValidators.Remove(plugin);
    }
}
```

Do not replace this with another reflection-based validation hook. Avalonia 12 no longer exposes the old plugin manipulation used here, and the prior repository migration demonstrates that removal is sufficient.

- [ ] **Step 3: Rebuild to isolate any remaining XAML compatibility failure**

Run:

```bash
"$DOTNET_ROOT/dotnet" build Downio.sln -c Debug --no-restore
```

Expected: either `Build succeeded.` or one remaining XAML diagnostic identifying `ExtendClientAreaChromeHints="PreferSystemChrome"` in the dialog style. Do not change other theme resources or selectors unless a diagnostic identifies them.

- [ ] **Step 4: Remove the obsolete dialog chrome setter only if Step 3 identifies it**

In `src/Downio/App.axaml`, change:

```xml
            <Setter Property="ExtendClientAreaToDecorationsHint" Value="True" />
            <Setter Property="ExtendClientAreaChromeHints" Value="PreferSystemChrome" />
            <Setter Property="ExtendClientAreaTitleBarHeightHint" Value="44" />
```

to:

```xml
            <Setter Property="ExtendClientAreaToDecorationsHint" Value="True" />
            <Setter Property="ExtendClientAreaTitleBarHeightHint" Value="44" />
```

If Step 3 already succeeds, leave `App.axaml` unchanged and record that Avalonia 12.1 accepts the existing setter.

- [ ] **Step 5: Run the Debug build until the compatibility test passes**

Run:

```bash
"$DOTNET_ROOT/dotnet" build Downio.sln -c Debug --no-restore
```

Expected: `Build succeeded.` with no new Avalonia migration warnings relative to the baseline. If a different failure remains, stop, invoke `superpowers:systematic-debugging`, identify its root cause, and add only the smallest evidence-backed migration.

- [ ] **Step 6: Review the focused diff and guard the scope**

Run:

```bash
git diff --check
git diff -- src/Downio/Downio.csproj src/Downio/App.axaml.cs src/Downio/App.axaml
python3 - <<'PY'
from pathlib import Path
project = Path('src/Downio/Downio.csproj').read_text()
assert '<Version>1.0.55</Version>' in project
assert 'Avalonia.Diagnostics' not in project
app = Path('src/Downio/App.axaml.cs').read_text()
assert 'BindingPlugins' not in app
assert 'DataAnnotationsValidationPlugin' not in app
print('migration scope checks passed')
PY
```

Expected: no whitespace errors, only the approved dependency and compatibility edits, and `migration scope checks passed`.

- [ ] **Step 7: Commit the dependency and compatibility migration**

Run:

```bash
git add src/Downio/Downio.csproj src/Downio/App.axaml.cs
if ! git diff --quiet -- src/Downio/App.axaml; then
  git add src/Downio/App.axaml
fi
git commit -m "chore: upgrade Avalonia to 12.1.0"
```

Expected: one commit containing only the package upgrade and required Avalonia 12 compatibility changes. Do not include the implementation plan or unrelated files in this commit.

### Task 3: Verify Debug, Release, and Native AOT Builds

**Files:**
- No source files expected to change
- Generated output: `/tmp/downio-avalonia-12.1-publish` (outside the repository)

- [ ] **Step 1: Clean generated outputs**

Run:

```bash
"$DOTNET_ROOT/dotnet" clean Downio.sln -c Debug
"$DOTNET_ROOT/dotnet" clean Downio.sln -c Release
rm -rf /tmp/downio-avalonia-12.1-publish
```

Expected: both clean commands succeed and the temporary publish directory is absent.

- [ ] **Step 2: Rebuild the complete solution in Debug from the clean state**

Run:

```bash
NUGET_MIRROR='https://repo.huaweicloud.com/repository/nuget/v3/index.json'
"$DOTNET_ROOT/dotnet" restore Downio.sln --source "$NUGET_MIRROR"
"$DOTNET_ROOT/dotnet" build Downio.sln -c Debug --no-restore
```

Expected: `Build succeeded.` Compare warnings with Task 1's baseline and investigate any new Avalonia-related warning.

- [ ] **Step 3: Build the complete solution in Release**

Run:

```bash
"$DOTNET_ROOT/dotnet" build Downio.sln -c Release --no-restore
```

Expected: `Build succeeded.` with no unresolved trimming, ILVerify, or Avalonia compatibility error.

- [ ] **Step 4: Publish Native AOT for the host macOS x64 RID**

Run:

```bash
"$DOTNET_ROOT/dotnet" publish src/Downio/Downio.csproj \
  -c Release \
  -r osx-x64 \
  --self-contained true \
  --no-restore \
  -o /tmp/downio-avalonia-12.1-publish \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true
```

Expected: publish succeeds, the output path is `/tmp/downio-avalonia-12.1-publish/`, and no unresolved AOT, trimmer, or ILVerify error remains.

- [ ] **Step 5: Confirm the native executable and required content exist**

Run:

```bash
test -x /tmp/downio-avalonia-12.1-publish/Downio
test -f /tmp/downio-avalonia-12.1-publish/Assets/Branding/app_icon.png
test -f /tmp/downio-avalonia-12.1-publish/Assets/cacert.pem
file /tmp/downio-avalonia-12.1-publish/Downio
```

Expected: all assertions pass and `file` identifies a native x86_64 Mach-O executable. If the exact asset layout differs because single-file publishing embeds an asset, inspect the publish output before changing project settings; do not disable trimming or AOT to force success.

- [ ] **Step 6: Confirm verification produced no source diff**

Run:

```bash
git status --short
git diff --check
```

Expected: no new source changes from build or publish. The plan document may remain separately uncommitted if it was not committed before execution.

### Task 4: Exercise the Application End-to-End on macOS x64

**Files:**
- No source files expected to change
- Runtime logs: capture outside the repository, for example `/tmp/downio-avalonia-12.1-debug.log`

- [ ] **Step 1: Launch the Debug application with captured diagnostics**

Use the project `run` skill so the real desktop application is launched and observed. Its underlying command is:

```bash
"$DOTNET_ROOT/dotnet" run \
  --project src/Downio/Downio.csproj \
  -c Debug \
  --no-build \
  > /tmp/downio-avalonia-12.1-debug.log 2>&1
```

Expected: the process remains running, the main window appears, and the log has no unhandled exception, XAML load failure, missing assembly, or missing-resource error.

- [ ] **Step 2: Verify the main window and themes**

In the running application:

1. Confirm the main window renders with navigation, task list, and command controls visible.
2. Open Settings.
3. Select Light and confirm foreground/background contrast and accent styling update.
4. Select Dark and confirm the same controls remain legible.
5. Select System and confirm the application follows the current macOS appearance.
6. Confirm no missing `SystemControl*` or `SystemAccentColor*` resource error appears in `/tmp/downio-avalonia-12.1-debug.log`.

Expected: all three theme modes render without a crash or visibly unstyled primary controls.

- [ ] **Step 3: Verify DataGrid, ColorPicker, and LiveMarkdown**

In the running application:

1. Inspect the task list and confirm the DataGrid renders columns, rows or its empty state, selection styling, and scroll behavior.
2. Open Settings and exercise the ColorPicker used for the accent setting; select a color and confirm the accent updates.
3. Open the update window through the application's available update flow and confirm release notes render as formatted markdown rather than raw text.

Expected: each third-party control renders and responds. If no update is available and the update window cannot be opened without changing application state, record LiveMarkdown as `not exercised` rather than claiming it passed.

- [ ] **Step 4: Verify macOS window and native integration behavior**

In the running application:

1. Drag the main window using its custom title-bar region.
2. Confirm the macOS traffic-light controls remain correctly positioned and usable.
3. Open the tray/native menu and confirm its icon and menu items appear.
4. Open the add-task file picker and cancel it.
5. Open the download-directory folder picker and cancel it.

Expected: title-bar dragging, native menu/tray integration, and both storage pickers work without an exception. If macOS does not expose a tray item in the current desktop configuration, record that exact limitation.

- [ ] **Step 5: Exit normally and inspect diagnostics**

Exit through the application's explicit exit action, then run:

```bash
python3 - <<'PY'
from pathlib import Path
log = Path('/tmp/downio-avalonia-12.1-debug.log')
text = log.read_text(errors='replace') if log.exists() else ''
needles = (
    'Unhandled exception',
    'XamlLoadException',
    'FileNotFoundException',
    'MissingMethodException',
    'Could not find resource',
)
hits = [needle for needle in needles if needle.lower() in text.lower()]
assert not hits, f'failure signatures found: {hits}'
print('no known startup/XAML/runtime failure signature found')
PY
```

Expected: `no known startup/XAML/runtime failure signature found` and the process exits without a crash report.

- [ ] **Step 6: Launch the published Native AOT executable**

Run through the project `run` skill:

```bash
/tmp/downio-avalonia-12.1-publish/Downio \
  > /tmp/downio-avalonia-12.1-aot.log 2>&1
```

Expected: the published application opens its main window. Confirm at least startup, main-window rendering, title-bar dragging, and normal exit; inspect `/tmp/downio-avalonia-12.1-aot.log` for the same failure signatures used in Step 5.

### Task 5: Perform the Final Scope and Evidence Review

**Files:**
- Verify only: `src/Downio/Downio.csproj`
- Verify only: `src/Downio/App.axaml.cs`
- Verify only if changed: `src/Downio/App.axaml`
- Verify unchanged: `VERSION`, `.github/workflows/`, `build/`, `tools/`

- [ ] **Step 1: Verify the committed file set and application version**

Run:

```bash
git show --stat --oneline HEAD
git show --format= --name-only HEAD
python3 - <<'PY'
from pathlib import Path
project = Path('src/Downio/Downio.csproj').read_text()
assert '<Version>1.0.55</Version>' in project
assert Path('VERSION').read_text().strip() == '1.0.55'
print('release version remains 1.0.55')
PY
```

Expected: the implementation commit contains `src/Downio/Downio.csproj`, `src/Downio/App.axaml.cs`, and optionally `src/Downio/App.axaml`; it contains no release, workflow, packaging, or feature files. The script prints `release version remains 1.0.55`.

- [ ] **Step 2: Re-run the final automated verification set**

Run:

```bash
NUGET_MIRROR='https://repo.huaweicloud.com/repository/nuget/v3/index.json'
"$DOTNET_ROOT/dotnet" restore Downio.sln --source "$NUGET_MIRROR"
"$DOTNET_ROOT/dotnet" build Downio.sln -c Debug --no-restore
"$DOTNET_ROOT/dotnet" build Downio.sln -c Release --no-restore
"$DOTNET_ROOT/dotnet" list src/Downio/Downio.csproj package --include-transitive
"$DOTNET_ROOT/dotnet" list src/Downio/Downio.csproj package --vulnerable --include-transitive
git diff --check
```

Expected: restore and both builds succeed, resolved package versions match the target set, no Avalonia.Diagnostics package appears, no new vulnerability or downgrade warning is introduced, and `git diff --check` is silent.

- [ ] **Step 3: Request a code review of the focused implementation diff**

Invoke `superpowers:requesting-code-review` and provide:

- the approved design at `docs/superpowers/specs/2026-07-12-avalonia-upgrade-design.md`;
- the implementation commit created in Task 2;
- the Debug, Release, Native AOT, managed-runtime, and AOT-runtime evidence;
- any smoke-test item explicitly marked `not exercised`.

Expected: no unresolved correctness finding. If review identifies a defect, use `superpowers:receiving-code-review`, verify the finding, implement only the confirmed fix, and repeat the affected verification.

- [ ] **Step 4: Report exact completion evidence and platform boundaries**

The completion report must state:

- Avalonia packages resolved at 12.1.0;
- LiveMarkdown.Avalonia resolved at 2.2.0;
- Avalonia.Diagnostics removed;
- Debug and Release build results and warning counts;
- osx-x64 Native AOT publish result;
- each managed and AOT smoke-test item as passed, failed, or not exercised;
- Windows and Linux runtime behavior remains unverified locally and is left to the existing six-RID CI/platform testing;
- application version remains 1.0.55;
- global NuGet settings, release metadata, tags, and unrelated functionality were not changed.

Do not state that the upgrade is complete if any build/publish step fails or if a required runtime check fails. A check that cannot be reached must be described explicitly rather than inferred.
