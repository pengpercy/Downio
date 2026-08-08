# Changelog

## 1.0.79-beta.3 - 2026-08-08

- Fixed the macOS download engine not starting because bundled content is stored under `Contents/Resources`.
- Kept the add-task dialog open and displayed an error notification when a download cannot be added.
- Simplified the beta update-channel label and visually aligned the version number on About pages.

## 1.0.79-beta.2 - 2026-08-08

- Fixed macOS binaries incorrectly requiring macOS 26.5 despite the package declaring macOS 12.0.
- Restored NativeAOT macOS publishing to reduce the installed app from about 167 MB to about 63 MB.
- Updated macOS packaging to extract and re-sign the SDK-generated native app bundle before creating ZIP and DMG packages.

## 1.0.79-beta.1 - 2026-08-08

- Added selectable stable and beta update channels, with beta users receiving both prerelease and stable updates.
- Added semantic prerelease version comparison and GitHub Actions prerelease publishing based on version tags.
- Targeted the native .NET 10 macOS workload and replaced legacy notification interop with `UNUserNotificationCenter`.
- Updated macOS packaging metadata and CI workload setup for `net10.0-macos`.

## 1.0.78 - 2026-08-08

- Replaced the bundled downloader with aria2-next 2.5.5 and added native ED2K support for search, direct links, network bootstrap data, and configurable engine/search settings.
- Added architecture-specific aria2-next binaries for macOS, Windows, and Linux, and placed them in conventional platform package locations.
- Added automatic and manual synchronization of ED2K server and Kad node bootstrap files with application and system proxy support.
- Added localized ED2K search results with file-type detection, icons, keyboard search, aligned metadata, and persistent search defaults.
- Fixed “Follow system” language switching after an explicit language selection and completed missing Chinese and English notifications.
- Improved dark-theme input focus colors and reduced Settings window startup cost by loading non-default sections on demand.
- Added third-party notices and bundled aria2-next license information.

## 1.0.77 - 2026-08-07

- Added support for standard uppercase proxy environment variables when launching aria2 while preserving lowercase variable precedence.
- Refreshed proxy environment settings when downloads are added or resumed, so proxy changes no longer require restarting Downio.
- Kept explicitly configured application proxy settings ahead of environment-provided proxies.

## 1.0.76 - 2026-07-19

- Restored native Windows caption button height for the main window by removing styles that stretched minimize/maximize/close to the full 52px title bar.

## 1.0.75 - 2026-07-18

- Fixed main window title bar double-click maximize on macOS racing with the native title bar handler, which caused the window to maximize and immediately restore.

## 1.0.74 - 2026-07-14

- Upgraded all Avalonia packages to the latest stable 12.1.0 release.
- Upgraded LiveMarkdown.Avalonia to 2.2.0 for Avalonia 12 compatibility.
- Updated window decoration configuration for the Avalonia 12 API.

## 1.0.73 - 2026-05-25

- Split close behavior so tray/menu quit and `Ctrl/Cmd+Q` exit the app immediately, while closing the main window still minimizes to the background.
- Focus the newly completed download automatically by switching to the completed list, selecting the finished task, and moving keyboard focus to it.
- Upgraded Avalonia packages to the latest stable 12.0.3 line where available while keeping `Avalonia.Controls.DataGrid` on its current latest stable 12.0.0 release.

## 1.0.72 - 2026-05-09

- Upgraded Avalonia packages to the latest stable 12.0.2 line where available.
- Fixed localization initialization so app resources and macOS native menu language follow the selected or system language reliably.
- Auto-filled the new-task link box from supported clipboard download links and avoided repeating the same automatic fill during the current app session.
- Focused the new-task link input automatically after clipboard autofill so pasted links can be confirmed or edited immediately.

## 1.0.71 - 2026-05-08

- Switched the macOS application menu to Avalonia's recommended setup by explicitly setting `Application.Name` to `Downio` and letting Avalonia append the standard native menu items.
- Removed duplicated custom app-menu items so the native macOS Hide and Quit entries come from the framework-managed application menu.

## 1.0.70 - 2026-05-08

- Forced the macOS process name to `Downio` so the native application menu no longer falls back to `Avalonia Application`.
- Restored main-window titlebar dragging on macOS after the Avalonia 12 titlebar hit-testing changes.
- Extended the Windows close button width by 1px so its hover background reaches the right window edge without a visible gap.

## 1.0.69 - 2026-05-08

- Restored the standard macOS application menu entries including Services, Hide, Hide Others, and Show All.
- Fixed the macOS application menu to use the Downio process name and removed duplicated custom About and Quit entries.
- Added macOS bundle localization resources for packaging and improved Windows extended title bar hit-testing so the close button hover background stays flush with the right edge.

## 1.0.68 - 2026-04-26

- Aligned Avalonia 12 caption buttons flush to the top-right corner on non-main windows without changing their default height.
- Restored Windows system notifications by binding the process AppUserModelID for Win10/Win11 toast delivery.
- Added a native Win32 tray balloon fallback for older Windows versions or failed toast delivery without launching external scripts.

## 1.0.67 - 2026-04-26

- Limited custom caption button height styling to the main window so other windows keep their default decoration height.
- Fixed update checks to use the configured proxy, select the highest stable GitHub release by version, and report check failures instead of saying the app is up to date.
- Reused an existing update window when an update is already open or downloading to avoid parallel update dialogs and progress flows.

## 1.0.66 - 2026-04-26

- Fixed the Windows main window hamburger button so it remains clickable inside the extended title bar.
- Tightened the Avalonia 12 caption button layout so minimize, maximize, and close align flush to the right edge.
- Removed the hidden full-screen caption button's remaining layout space on Windows.

## 1.0.65 - 2026-04-26

- Adjusted Avalonia 12 window decoration styling on Windows so caption button hover backgrounds fill the title bar height.
- Removed the main window native title text from the extended title bar to avoid overlapping the hamburger button.
- Hid the extra Avalonia 12 full-screen caption button, restoring the expected minimize, maximize, and close button set.

## 1.0.64 - 2026-04-26

- Hardened Windows startup so notification integration failures are logged instead of preventing the main window from opening.
- Improved single-instance activation handling to avoid silently exiting when an existing background instance cannot be activated.
- Added startup and unhandled exception logging to make Windows launch failures easier to diagnose.

## 1.0.63 - 2026-04-26

- Added a configurable default split count in Settings so new downloads can use the user's preferred chunk count.
- Changed new task creation to appear in the downloading list immediately while aria2 resolves the final task metadata.
- Improved split display behavior so the task list can show changing active split counts instead of a permanently fixed configured value.
- Avoided passing automatically guessed filenames to aria2, preventing dynamic or redirected downloads from failing because of unreliable local filename guesses.
- Logged aria2 error code and message details for failed tasks to make future download failures easier to diagnose.

## 1.0.62 - 2026-04-25

- Removed reflection from the Windows notification path to keep Native AOT publishing safe.
- Switched Windows builds to a Windows-specific target framework so native toast APIs can be called directly at compile time.

## 1.0.61 - 2026-04-25

- Switched the Windows native notification path to the underlying WinRT toast APIs for a more reliable release-build implementation.
- Removed dependence on the notification toolkit wrapper for toast delivery while keeping notifications in-process and script-free.

## 1.0.60 - 2026-04-25

- Fixed the Windows native notification implementation to use the toolkit API surface available in release builds.
- Corrected the Windows shortcut property-store call path so the release pipeline can compile and package successfully.

## 1.0.59 - 2026-04-25

- Finalized the Windows native notification migration with a packaging-safe implementation that no longer depends on command-line scripts.
- Simplified the Windows toast registration path to keep CI and release builds compatible across platforms.

## 1.0.58 - 2026-04-25

- Replaced Windows command-line toast invocation with an in-process native notification implementation to avoid antivirus false positives.
- Initialized Windows toast registration during app startup and created the required shortcut metadata for native notifications.

## 1.0.57 - 2026-04-25

- Hardened macOS packaging by creating DMGs from an isolated temporary source directory and retrying transient `hdiutil` failures.
- Re-ran the release flow for the Avalonia 12 upgrade, filename detection fixes, tracker selector refresh, and settings layout polish shipped in 1.0.56.

## 1.0.56 - 2026-04-25

- Upgraded Avalonia to 12.x and aligned `LiveMarkdown.Avalonia` to a compatible version.
- Fixed Avalonia 12 migration issues around clipboard APIs, window chrome properties, and placeholder text.
- Fixed automatic download filename detection by probing `Content-Disposition`, redirects, and final URLs more safely.
- Improved settings layout by aligning right-side controls consistently and adding subtle row dividers.
- Reworked the tracker source selector into a dropdown-style control with clearer summary text, localization, and better visual alignment.
- Resolved the vulnerable `Tmds.DBus.Protocol` dependency through the Avalonia 12 dependency graph.
