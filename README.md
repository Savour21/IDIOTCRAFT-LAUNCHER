# IDIOTCORD LAUNCHER

IDIOTCORD LAUNCHER is a Windows desktop launcher shell for Minecraft instances.

## Run the current app

Current version: **0.1.1**. See [CHANGELOG.md](CHANGELOG.md) for the complete history.

Build release installers locally or attach them to a GitHub Release. Do not
upload secrets or `launcher.settings.json`; users should create their own
configuration from `launcher.settings.json.example`.

Open `index.html` in a browser for the UI prototype, or build the Windows executable:

```powershell
dotnet build SavourLauncher.csproj -c Release
dotnet run --project SavourLauncher.csproj
```

The executable hosts the launcher UI locally. The logo file is expected at `savour-logo.png` beside the project files; the UI falls back to the Savour `S` mark until that file is added.

### Live update notices

Set `updateFeedUrl` in `launcher.settings.json` to a public raw HTTPS URL serving
the same shape as `launcher.update.json.example`. The launcher compares
semantic `major.minor.patch` versions and shows a startup decision prompt when
the feed reports a newer version. Use `updateType` values `patch`, `minor`, or
`major` for optional updates, or `required` to block launching until updated.

## Production integrations still required

### Microsoft and Minecraft login

Microsoft and Minecraft login is implemented in the Windows host. To enable it, register a Microsoft Entra application as a **public client** and add the **Mobile and desktop applications** redirect URI `http://localhost`. Then copy `launcher.settings.json.example` to `launcher.settings.json` and replace the value with the application (client) ID. Do not add a client secret.

The launcher opens the system browser and uses OAuth authorization-code flow with PKCE, then performs Xbox Live, XSTS, Minecraft Services, ownership, and Java profile verification. Its refresh token is encrypted with Windows DPAPI for the signed-in Windows user and stored at `%AppData%\\SavourLauncher\\account.json`.

### Modded instance launch

The native host should own instance installation and launch. A profile needs a Minecraft version, loader (Fabric/Forge/NeoForge), loader version, game directory, Java executable, JVM arguments, and resolved libraries/assets. Launch only after authenticating and resolving the version manifest and dependency graph.

### Accurate stats

The current UI reads local persisted counters and increments play time only after the launch flow. Production stats should be emitted by the native process manager: process start/exit, elapsed play time, resolved mod count, and bytes downloaded.
