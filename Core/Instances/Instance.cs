using System.Text.Json.Serialization;

namespace SavourLauncher.Core.Instances;

public sealed record Instance(
    string Id,
    string Name,
    string MinecraftVersion,
    string GameDirectory,
    LoaderSpec? Loader,
    string? JavaPath,
    MemorySettings Memory,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record LoaderSpec(string Type, string Version);
public sealed record MemorySettings(int MinimumMb, int MaximumMb);

public sealed record InstanceConfig(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("minecraftVersion")] string MinecraftVersion,
    [property: JsonPropertyName("gameDirectory")] string GameDirectory,
    [property: JsonPropertyName("loader")] LoaderSpec? Loader,
    [property: JsonPropertyName("javaPath")] string? JavaPath,
    [property: JsonPropertyName("memory")] MemorySettings Memory,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt);
