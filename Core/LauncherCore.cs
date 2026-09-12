using SavourLauncher.Core.Instances;
using SavourLauncher.Core.Java;
using SavourLauncher.Core.Minecraft;
using SavourLauncher.Core.Launcher;

namespace SavourLauncher.Core;

public sealed class LauncherCore
{
    public InstanceManager Instances { get; }
    public MinecraftManager Minecraft { get; }
    public JavaManager Java { get; } = new();
    public LaunchManager Launch { get; }

    public LauncherCore(string dataRoot)
    {
        var cache = Path.Combine(dataRoot, "cache");
        Instances = new InstanceManager(Path.Combine(dataRoot, "instances"));
        Minecraft = new MinecraftManager(cache);
        Launch = new LaunchManager(Minecraft, Java);
    }
}
