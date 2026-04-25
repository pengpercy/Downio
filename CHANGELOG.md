# Changelog

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
