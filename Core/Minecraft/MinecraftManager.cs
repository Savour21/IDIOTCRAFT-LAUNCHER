using System.Text.Json;
using System.IO.Compression;
using SavourLauncher.Core.Downloads;
using SavourLauncher.Core.Instances;

namespace SavourLauncher.Core.Minecraft;

public sealed record MinecraftVersion(string Id, string Type, DateTimeOffset ReleaseTime, string MetadataUrl);

public sealed class MinecraftManager
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private readonly HttpClient http;
    private readonly DownloadManager downloads;
    private readonly string cacheRoot;

    public MinecraftManager(string cacheRoot, HttpClient? httpClient = null)
    {
        this.cacheRoot = cacheRoot;
        http = httpClient ?? new HttpClient();
        downloads = new DownloadManager(http);
    }

    public async Task<IReadOnlyList<MinecraftVersion>> GetVersionsAsync(CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(await http.GetStringAsync(ManifestUrl, cancellationToken));
        return document.RootElement.GetProperty("versions").EnumerateArray().Select(version => new MinecraftVersion(
            version.GetProperty("id").GetString()!, version.GetProperty("type").GetString()!, version.GetProperty("releaseTime").GetDateTimeOffset(), version.GetProperty("url").GetString()!)).ToArray();
    }

    public async Task InstallAsync(Instance instance, CancellationToken cancellationToken = default)
    {
        var available = await GetVersionsAsync(cancellationToken);
        var version = available.FirstOrDefault(item => item.Id == instance.MinecraftVersion) ?? throw new InvalidOperationException($"Minecraft version {instance.MinecraftVersion} was not found in Mojang's official manifest.");
        var metadataPath = Path.Combine(cacheRoot, "minecraft", "versions", version.Id, $"{version.Id}.json");
        await downloads.DownloadAsync(new DownloadTask(version.MetadataUrl, metadataPath), cancellationToken);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath, cancellationToken));
        var root = document.RootElement;
        var versionDirectory = Path.Combine(cacheRoot, "minecraft", "versions", version.Id);
        var client = root.GetProperty("downloads").GetProperty("client");
        await downloads.DownloadAsync(new DownloadTask(client.GetProperty("url").GetString()!, Path.Combine(versionDirectory, $"{version.Id}.jar"), client.GetProperty("sha1").GetString(), null, client.GetProperty("size").GetInt64()), cancellationToken);
        await InstallLibrariesAsync(root, version.Id, cancellationToken);
        await InstallAssetsAsync(root, cancellationToken);
        Directory.CreateDirectory(instance.GameDirectory);
        await File.WriteAllTextAsync(Path.Combine(instance.GameDirectory, "installed-version.json"), root.GetRawText(), cancellationToken);
    }

    public string GetVersionMetadataPath(string version) => Path.Combine(cacheRoot, "minecraft", "versions", version, $"{version}.json");
    public string GetVersionJarPath(string version) => Path.Combine(cacheRoot, "minecraft", "versions", version, $"{version}.jar");
    public string GetLibrariesRoot() => Path.Combine(cacheRoot, "libraries");
    public string GetAssetsRoot() => Path.Combine(cacheRoot, "assets");
    public string GetNativesRoot(string version) => Path.Combine(cacheRoot, "natives", version);

    private async Task InstallLibrariesAsync(JsonElement root, string version, CancellationToken cancellationToken)
    {
        var nativesRoot = GetNativesRoot(version);
        Directory.CreateDirectory(nativesRoot);
        foreach (var library in root.GetProperty("libraries").EnumerateArray())
        {
            if (library.TryGetProperty("rules", out var rules) && !AllowedOnWindows(rules)) continue;
            if (!library.TryGetProperty("downloads", out var downloadsElement)) continue;
            if (downloadsElement.TryGetProperty("artifact", out var artifact))
            {
                var relative = artifact.GetProperty("path").GetString()!;
                await downloads.DownloadAsync(new DownloadTask(artifact.GetProperty("url").GetString()!, Path.Combine(cacheRoot, "libraries", relative), artifact.GetProperty("sha1").GetString(), null, artifact.GetProperty("size").GetInt64()), cancellationToken);
            }
            if (downloadsElement.TryGetProperty("classifiers", out var classifiers) && classifiers.TryGetProperty("natives-windows", out var native))
            {
                var relative = native.GetProperty("path").GetString()!;
                var archive = Path.Combine(cacheRoot, "libraries", relative);
                await downloads.DownloadAsync(new DownloadTask(native.GetProperty("url").GetString()!, archive, native.GetProperty("sha1").GetString(), null, native.GetProperty("size").GetInt64()), cancellationToken);
                ZipFile.ExtractToDirectory(archive, nativesRoot, true);
            }
        }
    }

    private async Task InstallAssetsAsync(JsonElement root, CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("assetIndex", out var index)) return;
        var indexId = index.GetProperty("id").GetString()!;
        var indexPath = Path.Combine(cacheRoot, "assets", "indexes", $"{indexId}.json");
        await downloads.DownloadAsync(new DownloadTask(index.GetProperty("url").GetString()!, indexPath, index.GetProperty("sha1").GetString(), null, index.GetProperty("size").GetInt64()), cancellationToken);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(indexPath, cancellationToken));
        foreach (var asset in document.RootElement.GetProperty("objects").EnumerateObject())
        {
            var hash = asset.Value.GetProperty("hash").GetString()!;
            await downloads.DownloadAsync(new DownloadTask($"https://resources.download.minecraft.net/{hash[..2]}/{hash}", Path.Combine(cacheRoot, "assets", "objects", hash[..2], hash), hash), cancellationToken);
        }
    }

    private static bool AllowedOnWindows(JsonElement rules)
    {
        var allowed = true;
        foreach (var rule in rules.EnumerateArray())
        {
            var applies = !rule.TryGetProperty("os", out var os) || !os.TryGetProperty("name", out var name) || name.GetString() == "windows";
            if (applies) allowed = rule.GetProperty("action").GetString() == "allow";
        }
        return allowed;
    }
}
