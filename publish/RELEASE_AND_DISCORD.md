# Release and Discord distribution

## Recommended download flow

1. Create a public GitHub repository for IDIOTCORD LAUNCHER.
2. Create a GitHub Release named `v0.1.0`.
3. Upload `publish-0.1.0/IDIOTCORDLauncher.exe` as the release asset.
4. Share the GitHub Releases page in Discord.

A GitHub Release is better than a Discord attachment because the executable is larger than Discord's normal free upload limit and the release URL stays stable.

Suggested Discord announcement:

```text
IDIOTCORD LAUNCHER v0.1.0 is available!
Download: https://github.com/YOUR-ACCOUNT/YOUR-REPOSITORY/releases/latest

This release includes the Windows launcher shell, optional Microsoft account setup,
Mojang version discovery, and the vanilla installation foundation.
```

## Enable live update notices

Host a JSON file at a stable HTTPS URL, for example through GitHub raw content:

```json
{
  "version": "0.1.0",
  "downloadUrl": "https://github.com/YOUR-ACCOUNT/YOUR-REPOSITORY/releases/latest/download/IDIOTCORDLauncher.exe",
  "notes": [
    "Initial IDIOTCORD LAUNCHER release."
  ]
}
```

Set that URL in the user's `launcher.settings.json`:

```json
{
  "clientId": "",
  "updateFeedUrl": "https://raw.githubusercontent.com/YOUR-ACCOUNT/YOUR-REPOSITORY/main/launcher.update.json"
}
```

For each release:

1. Update `version.json`.
2. Add a dated section to `CHANGELOG.md`.
3. Update the hosted feed's `version`, `downloadUrl`, and `notes`.
4. Publish the new executable as a GitHub Release asset.

The launcher compares semantic versions and only shows a download action when the
feed version is newer than the installed version. It does not download or replace
an executable silently.
