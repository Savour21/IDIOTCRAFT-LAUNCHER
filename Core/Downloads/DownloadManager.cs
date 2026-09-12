using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace SavourLauncher.Core.Downloads;

public sealed record DownloadTask(string Url, string Destination, string? Sha1 = null, string? Sha256 = null, long? Size = null);

public sealed class DownloadManager
{
    private readonly HttpClient http;
    private readonly SemaphoreSlim concurrency;

    public DownloadManager(HttpClient? httpClient = null, int maxConcurrentDownloads = 6)
    {
        http = httpClient ?? new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IDIOTCORD-LAUNCHER", "0.1"));
        concurrency = new SemaphoreSlim(Math.Max(1, maxConcurrentDownloads));
    }

    public async Task DownloadAsync(DownloadTask task, CancellationToken cancellationToken = default)
    {
        if (File.Exists(task.Destination) && await IsValidAsync(task, cancellationToken)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(task.Destination)!);
        await concurrency.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                var temporary = task.Destination + ".partial";
                try
                {
                    using var response = await http.GetAsync(task.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
                    await using (var output = File.Create(temporary))
                        await input.CopyToAsync(output, cancellationToken);
                    if (task.Size is not null && new FileInfo(temporary).Length != task.Size.Value) throw new InvalidDataException("Downloaded file size does not match metadata.");
                    if (!await IsValidAsync(task with { Destination = temporary }, cancellationToken)) throw new InvalidDataException("Downloaded file hash does not match metadata.");
                    File.Move(temporary, task.Destination, true);
                    return;
                }
                catch when (attempt < 3)
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                }
            }
        }
        finally { concurrency.Release(); }
    }

    private static async Task<bool> IsValidAsync(DownloadTask task, CancellationToken cancellationToken)
    {
        if (!File.Exists(task.Destination)) return false;
        var info = new FileInfo(task.Destination);
        if (task.Size is not null && info.Length != task.Size.Value) return false;
        if (task.Sha1 is null && task.Sha256 is null) return true;
        await using var stream = File.OpenRead(task.Destination);
        var hash = task.Sha256 is not null ? await SHA256.HashDataAsync(stream, cancellationToken) : await SHA1.HashDataAsync(stream, cancellationToken);
        var expected = task.Sha256 ?? task.Sha1!;
        return Convert.ToHexString(hash).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
