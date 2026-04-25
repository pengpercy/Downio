# Changelog

## 1.0.56 - 2026-04-25

- Upgraded Avalonia to 12.x and aligned `LiveMarkdown.Avalonia` to a compatible version.
- Fixed Avalonia 12 migration issues around clipboard APIs, window chrome properties, and placeholder text.
- Fixed automatic download filename detection by probing `Content-Disposition`, redirects, and final URLs more safely.
- Improved settings layout by aligning right-side controls consistently and adding subtle row dividers.
- Reworked the tracker source selector into a dropdown-style control with clearer summary text, localization, and better visual alignment.
- Resolved the vulnerable `Tmds.DBus.Protocol` dependency through the Avalonia 12 dependency graph.
