# IDIOTCORD LAUNCHER changelog

## 0.1.2 - 2026-09-12

- Added a startup update window with Update now and Continue options.
- Added patch, minor, major, and required update severity handling.
- Required updates disable launching until the release is installed.
- Connected the public GitHub update feed and release destination.
- Added an automated Windows build artifact for every GitHub push.

## 0.1.0 - 2026-09-12

- Rebranded the launcher to IDIOTCORD LAUNCHER.
- Added a Windows self-contained executable.
- Replaced the legacy Internet Explorer host with WebView2.
- Added isolated instance metadata and Minecraft game directories.
- Added Mojang version discovery and verified client/library/asset downloads.
- Added Java runtime detection and structured vanilla launch configuration.
- Added optional Microsoft account authentication through the native host.
- Added an optional HTTPS update feed and release download link.
- Updated Microsoft authentication configuration and synchronized launcher build settings.
- Fixed the Savour PNG logo rendering so the full portrait is visible.
- Added local update metadata fallback when no remote feed URL is configured.
- Expanded in-app release notes to reflect the latest launcher changes.

## Historical prototype work

The earlier prototype established the launcher dashboard, instance selection,
search, responsive layout, dark violet theme, supplied PNG logo, activity
statistics, and functional navigation views. These changes are consolidated
into the 0.1.0 release rather than being presented as separate public builds.