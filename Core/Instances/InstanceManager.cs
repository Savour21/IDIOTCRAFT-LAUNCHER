using System.Text.Json;

namespace SavourLauncher.Core.Instances;

public sealed class InstanceManager
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string rootDirectory;

    public InstanceManager(string rootDirectory)
    {
        this.rootDirectory = rootDirectory;
        Directory.CreateDirectory(rootDirectory);
    }

    public IReadOnlyList<Instance> List()
    {
        return Directory.EnumerateFiles(rootDirectory, "instance.json", SearchOption.AllDirectories)
            .Select(path => TryRead(path))
            .Where(instance => instance is not null)
            .Cast<Instance>()
            .OrderByDescending(instance => instance.UpdatedAt)
            .ToArray();
    }

    public Instance Create(string name, string minecraftVersion, LoaderSpec? loader = null)
    {
        var id = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var gameDirectory = Path.Combine(rootDirectory, id, "minecraft");
        var instance = new Instance(id, name, minecraftVersion, gameDirectory, loader, null, new MemorySettings(2048, 8192), now, now);
        Directory.CreateDirectory(gameDirectory);
        EnsureGameDirectories(gameDirectory);
        Save(instance);
        return instance;
    }

    public void Save(Instance instance)
    {
        EnsureGameDirectories(instance.GameDirectory);
        var file = Path.Combine(Path.GetDirectoryName(instance.GameDirectory)!, "instance.json");
        File.WriteAllText(file, JsonSerializer.Serialize(instance, JsonOptions));
    }

    public Instance? Get(string id)
    {
        var file = Path.Combine(rootDirectory, id, "instance.json");
        return File.Exists(file) ? TryRead(file) : null;
    }

    private static Instance? TryRead(string file)
    {
        try { return JsonSerializer.Deserialize<Instance>(File.ReadAllText(file), JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static void EnsureGameDirectories(string gameDirectory)
    {
        foreach (var directory in new[] { "mods", "config", "resourcepacks", "shaderpacks", "saves", "screenshots", "logs", "crash-reports", "datapacks" })
            Directory.CreateDirectory(Path.Combine(gameDirectory, directory));
    }
}
