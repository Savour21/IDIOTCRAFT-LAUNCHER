using System.Diagnostics;
using System.Text.Json;
using SavourLauncher.Core.Instances;
using SavourLauncher.Core.Java;
using SavourLauncher.Core.Minecraft;

namespace SavourLauncher.Core.Launcher;

public sealed record LaunchConfiguration(string Executable, IReadOnlyList<string> JvmArguments, IReadOnlyList<string> GameArguments, string MainClass, string WorkingDirectory);

public sealed class LaunchManager
{
    private readonly MinecraftManager minecraft;
    private readonly JavaManager java;

    public LaunchManager(MinecraftManager minecraft, JavaManager java)
    {
        this.minecraft = minecraft;
        this.java = java;
    }

    public async Task<Process> LaunchVanillaAsync(Instance instance, string accessToken, string playerName, string playerUuid, CancellationToken cancellationToken = default)
    {
        if (instance.Loader is not null && !instance.Loader.Type.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The selected instance uses {instance.Loader.Type}. Phase 1 currently supports vanilla installation and launch only.");
        var configuration = await BuildVanillaConfigurationAsync(instance, accessToken, playerName, playerUuid, cancellationToken);
        var startInfo = new ProcessStartInfo(configuration.Executable) { WorkingDirectory = configuration.WorkingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in configuration.JvmArguments.Concat(configuration.GameArguments)) startInfo.ArgumentList.Add(argument);
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Java could not be started.");
        return process;
    }

    public async Task<LaunchConfiguration> BuildVanillaConfigurationAsync(Instance instance, string accessToken, string playerName, string playerUuid, CancellationToken cancellationToken = default)
    {
        var metadataPath = minecraft.GetVersionMetadataPath(instance.MinecraftVersion);
        if (!File.Exists(metadataPath)) throw new InvalidOperationException($"Minecraft {instance.MinecraftVersion} is not installed for this instance. Install it before launching.");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath, cancellationToken));
        var root = document.RootElement;
        var runtime = await java.ResolveAsync(instance.MinecraftVersion, instance.JavaPath, cancellationToken);
        var libraries = root.GetProperty("libraries").EnumerateArray().Where(library => !library.TryGetProperty("rules", out var rules) || IsAllowed(rules)).SelectMany(GetArtifactPaths).Where(File.Exists).ToArray();
        var classpath = string.Join(Path.PathSeparator, libraries.Append(minecraft.GetVersionJarPath(instance.MinecraftVersion)));
        var natives = minecraft.GetNativesRoot(instance.MinecraftVersion);
        var values = new Dictionary<string, string>
        {
            ["auth_player_name"] = playerName, ["auth_uuid"] = playerUuid, ["auth_access_token"] = accessToken, ["user_type"] = "msa",
            ["version_name"] = instance.MinecraftVersion, ["version_type"] = root.GetProperty("type").GetString() ?? "release", ["game_directory"] = instance.GameDirectory,
            ["assets_root"] = minecraft.GetAssetsRoot(), ["assets_index_name"] = root.GetProperty("assetIndex").GetProperty("id").GetString()!, ["classpath"] = classpath,
            ["library_directory"] = minecraft.GetLibrariesRoot(), ["natives_directory"] = natives, ["launcher_name"] = "IDIOTCORD LAUNCHER", ["launcher_version"] = "0.1"
        };
        var jvm = new List<string> { $"-Xms{instance.Memory.MinimumMb}M", $"-Xmx{instance.Memory.MaximumMb}M", $"-Djava.library.path={natives}" };
        var game = new List<string>();
        if (root.TryGetProperty("arguments", out var arguments))
        {
            AddArguments(arguments.GetProperty("jvm"), values, jvm);
            AddArguments(arguments.GetProperty("game"), values, game);
        }
        else if (root.TryGetProperty("minecraftArguments", out var legacy)) game.AddRange(Expand(legacy.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries), values));
        jvm.Add("-cp"); jvm.Add(classpath);
        jvm.Add(root.GetProperty("mainClass").GetString()!);
        return new LaunchConfiguration(runtime.Path, jvm, game, root.GetProperty("mainClass").GetString()!, instance.GameDirectory);
    }

    private IEnumerable<string> GetArtifactPaths(JsonElement library)
    {
        if (!library.TryGetProperty("downloads", out var downloads)) yield break;
        if (downloads.TryGetProperty("artifact", out var artifact)) yield return Path.Combine(minecraft.GetLibrariesRoot(), artifact.GetProperty("path").GetString()!);
    }

    private static void AddArguments(JsonElement values, IReadOnlyDictionary<string, string> replacements, List<string> destination)
    {
        foreach (var argument in values.EnumerateArray())
        {
            if (argument.ValueKind == JsonValueKind.String) destination.AddRange(Expand(new[] { argument.GetString()! }, replacements));
            else if (argument.ValueKind == JsonValueKind.Object && argument.TryGetProperty("rules", out var rules) && IsAllowed(rules) && argument.TryGetProperty("value", out var value))
            {
                if (value.ValueKind == JsonValueKind.Array) destination.AddRange(Expand(value.EnumerateArray().Select(item => item.GetString()!), replacements));
                else destination.AddRange(Expand(new[] { value.GetString()! }, replacements));
            }
        }
    }

    private static IEnumerable<string> Expand(IEnumerable<string> arguments, IReadOnlyDictionary<string, string> replacements) => arguments.Select(argument => replacements.Aggregate(argument, (current, item) => current.Replace("${" + item.Key + "}", item.Value, StringComparison.Ordinal).Replace("{" + item.Key + "}", item.Value, StringComparison.Ordinal)));
    private static bool IsAllowed(JsonElement rules) { var allowed = true; foreach (var rule in rules.EnumerateArray()) { if (!rule.TryGetProperty("os", out var os) || !os.TryGetProperty("name", out var name) || name.GetString() == "windows") allowed = rule.GetProperty("action").GetString() == "allow"; } return allowed; }
}