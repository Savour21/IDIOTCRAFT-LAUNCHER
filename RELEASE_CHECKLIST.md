# Release checklist

1. Update `SavourLauncher.csproj`, `app.js`, `index.html`, `version.json`, and `launcher.update.json` to the same `major.minor.patch` version.
2. Set `updateType` to `patch`, `minor`, `major`, or `required` in `launcher.update.json`.
3. Add the user-facing changes to `CHANGELOG.md`; those notes appear in the startup update window.
4. Push to `main` and wait for the GitHub Actions Windows artifact.
5. Create a GitHub Release and attach the Windows ZIP. Keep the release URL in `launcher.update.json`.
6. Confirm the public raw feed returns the new version before distributing the build.