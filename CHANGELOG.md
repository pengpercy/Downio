# Changelog

## 1.0.97 - 2026-10-25

- On Linux the caption buttons now sit in the window's top-right corner, so their highlights reach the edges; they were inset by the space Avalonia reserves for its drawn shadow and frame.
- On Linux, launching Downio while it is already running now brings the existing window forward instead of starting a second copy.
- The main window is titled "Downio" again, so taskbars and window lists no longer show it as "Untitled window".
- On Linux, Settings has a new "Use custom title bar" switch to choose between Downio's own title bar (the default) and the window manager's. It takes effect after restarting Downio.

## 1.0.96 - 2026-10-24

- On Windows the red close-button highlight now reaches the window edge in the main and Settings windows, and the minimize, maximize, and close buttons sit flush against each other. The caption buttons kept a 2px spacing from the theme that left a gap at the right.
- Fixed creating the Start menu shortcut that Windows 10 needs to attribute Downio's notifications; it failed in the released (NativeAOT) builds.
- Opening Downio while it is already running now just brings the existing window forward, without logging a startup error from the second copy.
- The log now starts each session with the Downio version and Windows version.

## 1.0.95 - 2026-10-24

- Updated dependencies to their latest stable versions: LiveMarkdown.Avalonia 2.5.1 (release notes in the update window), System.Drawing.Common 10.0.13 on Windows, and Roslyn 5.9.0 for the build-time source generator.

## 1.0.94 - 2026-10-24

- On Windows 11 the main and Settings windows no longer leave a thin strip between the window content and the border, so the red close-button highlight reaches the window edge.
- Windows no longer shows the window title twice in the update windows and dialogs; the small left-aligned copy in the title bar is gone.

## 1.0.93 - 2026-10-24

- Added a separate Windows 7 package (`win7-x64`, also for Windows 8 and 8.1). It uses YY-Thunks and VC-LTL to stand in for the newer Windows APIs and runtime library that .NET 10 needs, ships the UCRT for the bundled aria2c, renders in software, and turns on TLS 1.2 so update checks reach GitHub. Automatic updates on Windows 7/8/8.1 pick this package; the regular Windows packages are unchanged.
- System notifications on Windows 7/8/8.1 now show as a tray balloon with Downio's icon, stay up long enough to be read, and log the reason when they can't be shown.
- Redesigned the New Task window: a segmented Link / Torrent switch, a fixed-height input area (a torrent drop zone in torrent mode), file name, save folder, and chunk count in a settings-style card, and a collapsible Advanced section for User-Agent, Authorization, Referer, Cookie, and proxy.
- Fixed a save folder picked for one new task carrying over to the next one; each new task starts from the folder set in Settings (#5).
- Redesigned the Settings window with grouped cards (App, Downloads, Other, Reset) and dividers between rows.
- On Linux, GNOME, KDE, and XFCE now get Downio's own title bar instead of the window manager's one stacked above it. Other desktops keep their native decorations; `DOWNIO_LINUX_CSD=0` or `1` overrides the choice.
- On macOS the window buttons sit centred in the taller title bar, including after a theme switch. If notifications are switched off for Downio in System Settings, the reason is now written to the log.
- Windows caption buttons, corner radii, and menus were refined; menu highlights have a margin and the ED2K search field uses a neutral focus border.

## 1.0.92 - 2026-10-24

- Moved the ED2K search field into the window toolbar, together with the file-type menu, sort menu, and view switch, so the page below shows only results. Selection actions and status sit in a slim bar at the bottom. The default save folder is now set only in Settings.
- Large ED2K result lists stay smooth: a re-sorted list is applied in one update instead of one move per row, and “Select all” no longer recounts the list for every row.
- Added a live download-speed graph above the Settings button in the sidebar, which scrolls smoothly instead of stepping once a second.
- Fixed the taskbar / Dock download-speed badge. On macOS the red Dock badge (for example “3.2M/s”) now appears reliably; on Windows the overlay is drawn at the taskbar’s real size for the current DPI, survives hiding to the tray and Explorer restarts, and appears when the app starts in the tray. The value is smoothed so it no longer jumps every second, and brief stalls don’t make it blink. The option is hidden on Linux, which has no badge.
- Rewrote the update flow: Check for Updates shows checking, “new version available” with release notes, Skip This Version / Remind Me Later / Install Update, then download progress (MB of MB), extraction, and Install and Relaunch. Downloads are verified against the release’s SHA-256 digest, and on macOS a failed install restores the previous app. With automatic updates on, the update is downloaded and prepared in the background.
- Redesigned the native menus: the macOS menu bar and the tray menu follow the app language (including Hide, Hide Others, and Show All) and gain File, Tasks, View, Window, and Help menus with keyboard shortcuts.
- Fixed system notifications on macOS (now using the User Notifications framework, with permission requested at launch) and made Windows toasts register the app properly so they show with Downio’s name and icon.
- Task-list file tiles no longer use status colors; files with an unknown type show the system file icon. Cards and ED2K results have smaller corner radii, drop-down highlights are rounded, and drop-down backgrounds are lighter.
- The sidebar uses the Mica material on Windows 11.
- Fixed “StatusStopped” and “StatusWaiting” appearing untranslated in the task list.
- Packaging: bundled data now lives in `Contents/Resources` on macOS and in a `resources` folder next to the executable on Windows and Linux. macOS packages keep only the target architecture of the native libraries, and oversized embedded images were shrunk and an unused component removed, making the macOS downloads about 25% smaller.

## 1.0.91 - 2026-10-23

- Upgraded the bundled download engine to aria2-next 3.0.8 (from 2.5.5) on all six platforms. The rewritten 3.x core downloads ED2K, Metalink, and web-seeded torrents considerably faster, uses less CPU and memory on HTTP downloads, and keeps going when servers rate-limit or misreport ranges. ED2K searches are now stopped with the engine's dedicated `removeEd2kSearch` call, so finishing a search no longer logs spurious RPC errors.
- Upgraded Avalonia to 12.1.4.
- Redesigned ED2K search: a single search field with an elapsed-time bar and a Stop button, file-type chips, and sorting by sources, size, or name. Results update in place while the search runs, instead of being rebuilt every poll.
- Added a compact table view for ED2K results (now the default) with name, type, size, source, and complete-source columns. Clicking a column header sorts by it. A switch next to the sort menu toggles between table and card views, and the choice is remembered.
- ED2K results support batch download: click rows to check them, then “Download selected”; double-click a row to download it immediately. Each row can copy its ED2K link, files already queued show “Added”, and the save folder can be changed from the search page.
- Added clearer ED2K empty states for the first visit, a search in progress, no results, and no results of the selected type.

## 1.0.90 - 2026-10-23

- Fixed “Open Folder” doing nothing for tasks that were added before aria2 resolved the file name: the task list now picks up the destination path, file list, and URL on every refresh. On Windows, files whose path contains spaces are now selected in Explorer correctly.
- Fixed tasks not being selectable in the task list: a click no longer gets toggled back off by the list’s built-in selection, and Ctrl/⌘-click, Shift-click, and Ctrl/⌘+A keep multi-selection intact.
- Fixed deleted downloading/waiting/paused tasks reappearing under Stopped: aria2’s leftover “removed” result is now purged, and deleting history-only tasks no longer reports a false error.
- The title-bar delete button is never disabled: it deletes the selected tasks (with a selection count badge), offers to clear the current list when nothing is selected, and shows a notice when the list is empty. The Delete key only acts on a selection. Multi-task confirmation messages are now localized.
- Redesigned the task list: file-type tiles tinted by status, a compact status/size/speed line, a hairline border with a soft shadow instead of the hard green hover shadow, a clear selected state (accent border, tint, and check badge), hover-revealed secondary actions, and a pause/resume button only on tasks that can be paused or resumed. The context menu gains Open Folder and Copy Link.
- Redesigned the task details window with an overview card, transfer metrics, and copyable link/path/ID rows plus an Open Folder button.
- The completed status color is now #33D158.
- File and folder pickers use localized titles. On macOS, native panels follow the app’s language setting (after a restart), and debug builds now ship the `.lproj` folders so the open panel sidebar is localized when running from `bin/`.

## 1.0.89 - 2026-10-04

- Fixed the task-list “Open Folder” action when a download file is missing or has not been created: macOS now opens the nearest existing containing directory instead of silently failing to reveal the file; Windows also falls back to opening the directory.
- Folder launcher failures now produce an error notification and diagnostic log on macOS and Linux.

## 1.0.88 - 2026-09-19

- Fixed the task-list “Open Folder” button not invoking its command. The action now uses a direct click handler so it reliably receives the row’s download task, and reports an error when the destination cannot be opened.

## 1.0.87 - 2026-09-19

- Fixed the per-task “Open Folder” action on macOS and other platforms by passing the target path as a process argument rather than embedding shell quotes; files and directories with spaces are now revealed correctly.
- Fixed title-bar download controls, including Remove Selected, being treated as drag targets on macOS. They are now explicitly interactive and clicks reach their commands.

## 1.0.86 - 2026-09-06

- The app now detects the OS-level system proxy as a lowest-priority fallback: on macOS it reads the SystemConfiguration proxy state (via `scutil --proxy`), on Windows the WinINET registry settings. Proxy tools such as Clash and Surge that toggle the system proxy without exporting `http_proxy` / `https_proxy` environment variables now work out of the box.
- Proxy resolution now follows a clear priority chain: per-task proxy > in-app proxy settings > environment variables > OS system proxy, for aria2 downloads, update checks, filename detection, tracker sync, and ED2K bootstrap alike.
- macOS proxy exceptions (ExceptionsList) and Windows `ProxyOverride` entries are mapped to aria2's `no-proxy` option so local addresses bypass the proxy.
- System proxy detection is cached briefly (5 seconds) so proxy on/off switches in Clash/Surge are picked up quickly without spawning helper processes on every request.

## 1.0.85 - 2026-09-05

- Fixed `https_proxy` and related system environment variables not being honored by the application's own HTTP traffic (update checks, filename detection, tracker sync, and ED2K bootstrap downloads). These requests now fall back to the environment proxy when no in-app proxy is configured.
- On macOS, proxy variables exported in shell profiles (`.zshrc`, `.zshenv`, `.zprofile`, `.bash_profile`, `.bashrc`, `.profile`) are now read when the app is launched outside a shell and the variables are not otherwise set.
- Tracker synchronization now also honors SOCKS5 proxies instead of silently skipping them.

## 1.0.84 - 2026-09-05

- Fixed system proxy environment handling for aria2: `https_proxy`, `HTTPS_PROXY`, and related variables are now resolved consistently at startup and when downloads refresh their proxy settings.
- On macOS, merge `launchctl` and process proxy environments instead of discarding process variables when `launchctl` contains only a partial proxy configuration.
- Recognize lowercase user- and machine-level proxy environment variables on Windows.

## 1.0.83 - 2026-08-16

- Fixed macOS automatic updates leaving a `Downio.app.bak` folder behind: the updater now removes the backup only after the replacement app has been copied and launched successfully.
- Disabled NuGet vulnerability auditing for offline local builds to prevent `NU1900` source-connectivity warnings, while retaining the audit in CI builds.

## 1.0.82 - 2026-08-09

- Persist completed, stopped, and failed download entries locally so the Stopped view remains populated after aria2 and Downio restart; deleting an entry or resetting the download session clears the corresponding history.
- Fixed Windows taskbar speed overlays by retaining each native overlay icon until Windows replaces or clears it, and made the compact speed label readable at taskbar icon size.
- Allow deletion of a locally persisted stopped task's files even when its in-memory aria2 result is no longer available after restart.

## 1.0.81 - 2026-08-09

- Fixed local-file deletion on Windows: wait briefly for aria2 to release file handles, retry locked deletions, and show an error rather than a false success notification when a file remains.
- Delete every file belonging to multi-file torrent and Metalink tasks, including each `.aria2` control file.
- Fixed the Windows taskbar speed badge under NativeAOT by replacing unsupported `[ComImport]` activation with direct TaskbarList3 COM activation.

## 1.0.80 - 2026-08-09

- Fixed Windows download-engine startup: aria2-next now uses the Windows certificate store instead of receiving an unsupported custom CA bundle, restoring task creation and downloads.

## 1.0.79 - 2026-08-09

- Added live aggregate download-speed badges on the Windows taskbar and macOS Dock.
- Added a General settings switch to show or hide the badge; it is enabled by default and updates immediately.
- Displayed explicit per-second speed units on the badge, such as `9.5 M/s`.

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
