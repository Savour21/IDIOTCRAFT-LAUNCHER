# IDIOTCORD Prism Fork

This directory is a source fork of Prism Launcher. It remains licensed under
the GNU GPL v3.0-only; preserve the license, copyright notices, and source
availability when distributing modified binaries.

Prism-specific credentials have been disabled in `CMakeLists.txt`:

- Microsoft Identity client ID: empty until IDIOTCORD obtains its own approved registration.
- CurseForge API key: empty until IDIOTCORD obtains its own key and accepts the provider terms.
- Imgur client ID: empty until IDIOTCORD registers its own application.

Do not copy Prism's Microsoft, CurseForge, or other API credentials into an
IDIOTCORD build. A fork must be clearly identified as independent and not
endorsed by Prism Launcher. The Prism name, logos, and other assets may have
separate licensing requirements from the GPL-covered source.

Build this fork separately from the existing C# prototype. The current
prototype remains in the parent directory until the Prism fork has a verified
build and IDIOTCORD-specific providers are implemented.