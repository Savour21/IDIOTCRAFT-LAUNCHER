using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SavourLauncher.Core.Java;

public sealed record JavaRuntime(string Path, Version Version, int MajorVersion);

public sealed class JavaManager
{
    public async Task<IReadOnlyList<JavaRuntime>> DetectAsync(CancellationToken cancellationToken = default)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var executable = Path.Combine(directory, OperatingSystem.IsWindows() ? "java.exe" : "java");
            if (File.Exists(executable)) candidates.Add(executable);
        }
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome)) candidates.Add(Path.Combine(javaHome, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java"));
        var results = new List<JavaRuntime>();
        foreach (var candidate in candidates)
        {
            var runtime = await ReadVersionAsync(candidate, cancellationToken);
            if (runtime is not null) results.Add(runtime);
        }
        return results.OrderByDescending(runtime => runtime.MajorVersion).ToArray();
    }

    public async Task<JavaRuntime> ResolveAsync(string minecraftVersion, string? preferredPath = null, CancellationToken cancellationToken = default)
    {
        var runtimes = await DetectAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            var preferred = runtimes.FirstOrDefault(runtime => string.Equals(runtime.Path, preferredPath, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null && IsCompatible(minecraftVersion, preferred.MajorVersion)) return preferred;
        }
        var selected = runtimes.FirstOrDefault(runtime => IsCompatible(minecraftVersion, runtime.MajorVersion));
        if (selected is null) throw new InvalidOperationException($"No compatible Java runtime was found for Minecraft {minecraftVersion}. Minecraft 1.20.5 and newer require Java 21; older versions generally require Java 17.");
        return selected;
    }

    private static bool IsCompatible(string minecraftVersion, int javaMajor)
    {
        var parts = minecraftVersion.Split('.');
        var minor = parts.Length > 1 && int.TryParse(parts[1], out var parsed) ? parsed : 0;
        var patch = parts.Length > 2 && int.TryParse(parts[2], out var parsedPatch) ? parsedPatch : 0;
        var required = minor >= 21 || minor == 20 && patch >= 5 ? 21 : 17;
        return javaMajor >= required;
    }

    private static async Task<JavaRuntime?> ReadVersionAsync(string executable, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(executable, "-version") { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            if (process is null) return null;
            var output = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var match = Regex.Match(output, "version \\\"(?<version>[^\\\"]+)");
            if (!match.Success) return null;
            var versionText = match.Groups["version"].Value;
            var majorText = versionText.StartsWith("1.") ? versionText.Split('.')[1] : versionText.Split('.')[0];
            var normalizedVersion = versionText.Split('-')[0].Replace('_', '.');
            return int.TryParse(majorText, out var major) && Version.TryParse(normalizedVersion, out var version) ? new JavaRuntime(executable, version, major) : null;
        }
        catch { return null; }
    }
}
