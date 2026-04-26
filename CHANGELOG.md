# Changelog

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
