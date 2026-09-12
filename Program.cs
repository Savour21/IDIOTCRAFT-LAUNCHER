using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using SavourLauncher.Core;

namespace SavourLauncher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new LauncherWindow());
    }
}

[ComVisible(true)]
public sealed class LauncherBridge(LauncherWindow window)
{
    public void beginWindowDrag() => window.BeginWindowDrag();
    public void minimizeWindow() => window.MinimizeWindow();
    public void toggleMaximizeWindow() => window.ToggleMaximizeWindow();
    public void closeWindow() => window.CloseWindow();
    public void microsoftLogin() => window.BeginMicrosoftLogin();
    public void microsoftLogout() => window.LogoutMicrosoft();
    public void installVanilla(string name, string version) => window.InstallVanilla(name, version);
    public void launchSelected(string name, string version, string loader) => window.LaunchSelected(name, version, loader);
    public void getMinecraftVersions() => window.GetMinecraftVersions();
    public void checkForUpdates() => window.CheckForUpdates();
}

public sealed class LauncherWindow : Form
{
    private const string MicrosoftAuthority = "https://login.microsoftonline.com/common/oauth2/v2.0";
    private static readonly HttpClient Http = new();
    private readonly LauncherBridge bridge;
    private readonly WebView2 browser = new()
    {
        Dock = DockStyle.Fill
    };
    private readonly LauncherSettings settings;
    private readonly LauncherCore core;
    private AccountRecord? account;
    private string? minecraftAccessToken;
    private bool pageReady;

    public LauncherWindow()
    {
        settings = LauncherSettings.Load();
        core = new LauncherCore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IDIOTCORDLauncher"));
        bridge = new LauncherBridge(this);
        Text = "IDIOTCORD LAUNCHER";
        FormBorderStyle = FormBorderStyle.None;
        Width = 1440;
        Height = 920;
        MinimumSize = new Size(980, 650);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(16, 13, 24);
        Controls.Add(browser);
        FormClosed += (_, _) =>
        {
            browser.Dispose();
            Application.ExitThread();
        };
        Shown += async (_, _) => await InitializeBrowserAsync();
    }

    public void BeginWindowDrag()
    {
        if (WindowState == FormWindowState.Maximized) return;
        ReleaseCapture();
        SendMessage(Handle, WindowMessage, CaptionHitTest, 0);
    }

    public void MinimizeWindow() => WindowState = FormWindowState.Minimized;
    public void ToggleMaximizeWindow() => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
    public void CloseWindow()
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(CloseWindow); return; }
        Close();
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint handle, int message, nint wParam, nint lParam);

    private const int WindowMessage = 0xA1;
    private static readonly nint CaptionHitTest = 2;

    private async Task InitializeBrowserAsync()
    {
        await browser.EnsureCoreWebView2Async();
        browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
        browser.CoreWebView2.AddHostObjectToScript("bridge", bridge);
        browser.CoreWebView2.WebMessageReceived += HandleWebMessage;
        browser.CoreWebView2.NavigationStarting += HandleNavigation;
        browser.CoreWebView2.NavigationCompleted += HandleDocumentCompleted;
        browser.CoreWebView2.Navigate(new Uri(Path.Combine(AppContext.BaseDirectory, "index.html")).AbsoluteUri);
    }

    private void HandleWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var message = JsonDocument.Parse(e.WebMessageAsJson);
        if (!message.RootElement.TryGetProperty("type", out var type)) return;
        switch (type.GetString())
        {
            case "begin-window-drag": BeginWindowDrag(); break;
            case "minimize-window": MinimizeWindow(); break;
            case "toggle-maximize-window": ToggleMaximizeWindow(); break;
            case "close-window": CloseWindow(); break;
        }
    }

    private async void HandleDocumentCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess) return;
        pageReady = true;
        var drive = new DriveInfo(Path.GetPathRoot(AppContext.BaseDirectory)!);
        await ExecuteScriptAsync("setStorageStats", drive.TotalSize, drive.AvailableFreeSpace);
        await RestoreSavedAccountAsync();
    }

    private static void HandleNavigation(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            e.Cancel = true;
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }

    private async Task ExecuteScriptAsync(string function, params object[] args)
    {
        if (browser.CoreWebView2 is null) return;
        var serialized = string.Join(",", args.Select(arg => JsonSerializer.Serialize(arg)));
        await browser.CoreWebView2.ExecuteScriptAsync($"window.{function}({serialized});");
    }

    public async void BeginMicrosoftLogin()
    {
        if (!settings.IsConfigured)
        {
            ReportLoginState("error", "Microsoft login is not configured. Add your Entra application client ID to launcher.settings.json.");
            return;
        }

        ReportLoginState("working", "Opening Microsoft sign-in in your browser…");
        try
        {
            var result = await AuthenticateInteractiveAsync();
            account = new AccountRecord(result.ProfileName, result.ProfileId, Protect(result.RefreshToken));
            minecraftAccessToken = result.AccessToken;
            SaveAccount(account);
            ReportLoginState("signed-in", $"Signed in as {result.ProfileName}", result.ProfileName);
        }
        catch (Exception ex)
        {
            ReportLoginState("error", FriendlyError(ex));
        }
    }

    public void LogoutMicrosoft()
    {
        account = null;
        minecraftAccessToken = null;
        if (File.Exists(AccountFilePath)) File.Delete(AccountFilePath);
        ReportLoginState("signed-out", "No Microsoft account connected.");
    }

    private async Task RestoreSavedAccountAsync()
    {
        if (!settings.IsConfigured || account is not null) return;
        try
        {
            account = LoadAccount();
            if (account is null) return;
            ReportLoginState("working", "Restoring Microsoft account…");
            var refreshed = await RefreshMicrosoftTokenAsync(Unprotect(account.ProtectedRefreshToken));
            var minecraft = await CompleteMinecraftLoginAsync(refreshed.AccessToken);
            account = account with { ProfileName = minecraft.ProfileName, ProfileId = minecraft.ProfileId, ProtectedRefreshToken = Protect(refreshed.RefreshToken) };
            minecraftAccessToken = minecraft.AccessToken;
            SaveAccount(account);
            ReportLoginState("signed-in", $"Signed in as {minecraft.ProfileName}", minecraft.ProfileName);
        }
        catch
        {
            account = null;
            if (File.Exists(AccountFilePath)) File.Delete(AccountFilePath);
            ReportLoginState("signed-out", "Sign in to connect your Minecraft account.");
        }
    }

    private async Task<MinecraftLogin> AuthenticateInteractiveAsync()
    {
        var verifier = CreateCodeVerifier();
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var listener = await StartLoopbackListenerAsync();
        var redirectUri = listener.Prefixes.Single().TrimEnd('/');
        var authorize = new UriBuilder($"{MicrosoftAuthority}/authorize")
        {
            Query = FormEncode(new Dictionary<string, string>
            {
                ["client_id"] = settings.ClientId, ["response_type"] = "code", ["redirect_uri"] = redirectUri,
                ["response_mode"] = "query", ["scope"] = "XboxLive.signin offline_access", ["state"] = state,
                ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))), ["code_challenge_method"] = "S256", ["prompt"] = "select_account"
            })
        }.Uri;

        Process.Start(new ProcessStartInfo(authorize.AbsoluteUri) { UseShellExecute = true });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var context = await listener.GetContextAsync().WaitAsync(cancellation.Token);
        await WriteBrowserResponseAsync(context.Response);
        var query = ParseQuery(context.Request.Url?.Query ?? "");
        if (!query.TryGetValue("state", out var returnedState) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(returnedState)))
            throw new InvalidOperationException("Microsoft sign-in could not be verified. Please try again.");
        if (query.TryGetValue("error_description", out var error)) throw new InvalidOperationException(error);
        if (!query.TryGetValue("code", out var code)) throw new InvalidOperationException("Microsoft did not return an authorization code.");

        var token = await RequestMicrosoftTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = settings.ClientId, ["grant_type"] = "authorization_code", ["code"] = code,
            ["redirect_uri"] = redirectUri, ["code_verifier"] = verifier,
            ["scope"] = "XboxLive.signin offline_access"
        });
        var minecraft = await CompleteMinecraftLoginAsync(token.AccessToken);
        return minecraft with { RefreshToken = token.RefreshToken };
    }

    private async Task<MicrosoftToken> RefreshMicrosoftTokenAsync(string refreshToken) => await RequestMicrosoftTokenAsync(new Dictionary<string, string>
    {
        ["client_id"] = settings.ClientId, ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken, ["scope"] = "XboxLive.signin offline_access"
    });

    private static async Task<MicrosoftToken> RequestMicrosoftTokenAsync(Dictionary<string, string> values)
    {
        using var response = await Http.PostAsync($"{MicrosoftAuthority}/token", new FormUrlEncodedContent(values));
        var json = await ReadJsonOrThrowAsync(response, "Microsoft token request");
        return new MicrosoftToken(json.GetProperty("access_token").GetString()!, json.GetProperty("refresh_token").GetString()!);
    }

    private static async Task<MinecraftLogin> CompleteMinecraftLoginAsync(string microsoftAccessToken)
    {
        var xbl = await PostJsonAsync("https://user.auth.xboxlive.com/user/authenticate", new { Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + microsoftAccessToken }, RelyingParty = "http://auth.xboxlive.com", TokenType = "JWT" }, "Xbox Live authentication");
        var xblToken = xbl.GetProperty("Token").GetString()!;
        var userHash = xbl.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString()!;
        var xsts = await PostJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize", new { Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xblToken } }, RelyingParty = "rp://api.minecraftservices.com/", TokenType = "JWT" }, "Xbox security authentication");
        var xstsToken = xsts.GetProperty("Token").GetString()!;
        var minecraft = await PostJsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox", new { identityToken = $"XBL3.0 x={userHash};{xstsToken}" }, "Minecraft authentication");
        var minecraftToken = minecraft.GetProperty("access_token").GetString()!;
        using var entitlementRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/entitlements/mcstore");
        entitlementRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", minecraftToken);
        using var entitlementResponse = await Http.SendAsync(entitlementRequest);
        var entitlements = await ReadJsonOrThrowAsync(entitlementResponse, "Minecraft ownership check");
        if (!entitlements.TryGetProperty("items", out var items) || items.GetArrayLength() == 0) throw new InvalidOperationException("This Microsoft account does not own Minecraft: Java Edition.");
        using var profileRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
        profileRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", minecraftToken);
        using var profileResponse = await Http.SendAsync(profileRequest);
        var profile = await ReadJsonOrThrowAsync(profileResponse, "Minecraft profile request");
        return new MinecraftLogin(profile.GetProperty("name").GetString()!, profile.GetProperty("id").GetString()!, "", minecraftToken);
    }

    private static async Task<JsonElement> PostJsonAsync(string url, object body, string operation)
    {
        using var response = await Http.PostAsync(url, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
        return await ReadJsonOrThrowAsync(response, operation);
    }

    private static async Task<JsonElement> ReadJsonOrThrowAsync(HttpResponseMessage response, string operation)
    {
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{operation} failed ({(int)response.StatusCode}): {content[..Math.Min(content.Length, 240)]}");
        using var document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }

    private static async Task<HttpListener> StartLoopbackListenerAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{Random.Shared.Next(49152, 65535)}/");
            try { listener.Start(); return listener; } catch (HttpListenerException) { listener.Close(); }
        }
        await Task.CompletedTask;
        throw new InvalidOperationException("Could not start a local sign-in callback listener.");
    }

    private static async Task WriteBrowserResponseAsync(HttpListenerResponse response)
    {
        const string body = "<!doctype html><title>IDIOTCORD LAUNCHER</title><body style='font-family:system-ui;background:#100d18;color:#f0eafa;padding:3rem'><h2>You may return to IDIOTCORD LAUNCHER.</h2><p>Your sign-in response was received securely.</p></body>";
        var bytes = Encoding.UTF8.GetBytes(body);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    public async void InstallVanilla(string name, string version)
    {
        try
        {
            var instance = core.Instances.List().FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? core.Instances.Create(name, version);
            ReportLaunchState("working", $"Installing Minecraft {version}...");
            await core.Minecraft.InstallAsync(instance);
            ReportLaunchState("ready", $"Minecraft {version} is installed and ready.");
        }
        catch (Exception exception) { ReportLaunchState("error", exception.Message); }
    }

    public async void GetMinecraftVersions()
    {
        try
        {
            var versions = await core.Minecraft.GetVersionsAsync();
            if (pageReady)
                await ExecuteScriptAsync("setMinecraftVersions", string.Join("|", versions.Where(version => version.Type == "release").Select(version => version.Id)));
        }
        catch (Exception exception) { ReportLaunchState("error", $"Could not retrieve Minecraft versions: {exception.Message}"); }
    }

    public async void CheckForUpdates()
    {
        try
        {
            var feed = await ReadUpdateFeedAsync();
            var current = await ReadLocalVersionAsync();
            var latest = feed.GetProperty("version").GetString() ?? current;
            var updateAvailable = CompareVersions(latest, current) > 0;
            var updateType = feed.TryGetProperty("updateType", out var type) ? type.GetString() ?? "patch" : InferUpdateType(latest, current);
            var required = string.Equals(updateType, "required", StringComparison.OrdinalIgnoreCase);
            await ExecuteScriptAsync("setUpdateNotice", updateAvailable ? "available" : "current", latest, feed.TryGetProperty("downloadUrl", out var url) ? url.GetString() ?? "" : "", feed.TryGetProperty("notes", out var notes) ? string.Join(" ", notes.EnumerateArray().Select(note => note.GetString())) : "", required ? "required" : NormalizeUpdateType(updateType));
        }
        catch (Exception exception) { ReportLaunchState("error", $"Update check failed: {exception.Message}"); }
    }

    private async Task<JsonElement> ReadUpdateFeedAsync()
    {
        if (!string.IsNullOrWhiteSpace(settings.UpdateFeedUrl))
        {
            using var response = await Http.GetAsync(settings.UpdateFeedUrl);
            return await ReadJsonOrThrowAsync(response, "Update check");
        }

        var localPath = Path.Combine(AppContext.BaseDirectory, "launcher.update.json");
        if (!File.Exists(localPath))
            throw new InvalidOperationException("No update feed is configured. Set updateFeedUrl in launcher.settings.json or add launcher.update.json beside the launcher.");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(localPath));
        return document.RootElement.Clone();
    }

    private static async Task<string> ReadLocalVersionAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "version.json");
        if (!File.Exists(path)) return "0.1.0";
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        return document.RootElement.GetProperty("version").GetString() ?? "0.1.0";
    }

    private static int CompareVersions(string left, string right)
    {
        var leftParts = left.Split('.').Select(part => int.TryParse(part, out var value) ? value : 0).ToArray();
        var rightParts = right.Split('.').Select(part => int.TryParse(part, out var value) ? value : 0).ToArray();
        for (var index = 0; index < Math.Max(leftParts.Length, rightParts.Length); index++)
        {
            var leftValue = index < leftParts.Length ? leftParts[index] : 0;
            var rightValue = index < rightParts.Length ? rightParts[index] : 0;
            if (leftValue != rightValue) return leftValue.CompareTo(rightValue);
        }
        return 0;
    }

    private static string InferUpdateType(string latest, string current)
    {
        var latestParts = latest.Split('.').Select(part => int.TryParse(part, out var value) ? value : 0).ToArray();
        var currentParts = current.Split('.').Select(part => int.TryParse(part, out var value) ? value : 0).ToArray();
        if (latestParts[0] != currentParts[0]) return "major";
        if (latestParts.Length > 1 && currentParts.Length > 1 && latestParts[1] != currentParts[1]) return "minor";
        return "patch";
    }

    private static string NormalizeUpdateType(string updateType) => updateType.ToLowerInvariant() switch
    {
        "major" => "major",
        "minor" => "minor",
        "required" => "required",
        _ => "patch"
    };

    public async void LaunchSelected(string name, string version, string loader)
    {
        if (!loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
        {
            ReportLaunchState("error", $"{loader} launching is not available yet. Phase 1 supports vanilla Minecraft only.");
            return;
        }
        try
        {
            if (account is null) throw new InvalidOperationException("Connect a Microsoft Minecraft account before launching.");
            var token = await GetMinecraftAccessTokenAsync();
            var instance = core.Instances.List().FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? core.Instances.Create(name, version);
            if (!File.Exists(core.Minecraft.GetVersionMetadataPath(version)))
            {
                ReportLaunchState("working", $"Installing Minecraft {version}...");
                await core.Minecraft.InstallAsync(instance);
            }
            ReportLaunchState("working", "Starting Minecraft...");
            var process = await core.Launch.LaunchVanillaAsync(instance, token, account.ProfileName, account.ProfileId);
            process.OutputDataReceived += (_, args) => { if (!string.IsNullOrWhiteSpace(args.Data)) ReportLaunchState("log", args.Data); };
            process.ErrorDataReceived += (_, args) => { if (!string.IsNullOrWhiteSpace(args.Data)) ReportLaunchState("log", args.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _ = process.WaitForExitAsync().ContinueWith(_ => ReportLaunchState(process.ExitCode == 0 ? "stopped" : "crashed", process.ExitCode == 0 ? "Minecraft exited." : $"Minecraft exited with code {process.ExitCode}."));
        }
        catch (Exception exception) { ReportLaunchState("error", exception.Message); }
    }

    private async Task<string> GetMinecraftAccessTokenAsync()
    {
        if (minecraftAccessToken is not null) return minecraftAccessToken;
        if (account is null) throw new InvalidOperationException("No Microsoft account is connected.");
        var refreshed = await RefreshMicrosoftTokenAsync(Unprotect(account.ProtectedRefreshToken));
        var minecraft = await CompleteMinecraftLoginAsync(refreshed.AccessToken);
        minecraftAccessToken = minecraft.AccessToken;
        account = account with { ProfileName = minecraft.ProfileName, ProfileId = minecraft.ProfileId, ProtectedRefreshToken = Protect(refreshed.RefreshToken) };
        SaveAccount(account);
        return minecraftAccessToken;
    }

    private void ReportLaunchState(string state, string message)
    {
        if (!pageReady) return;
        if (InvokeRequired) { BeginInvoke(() => ReportLaunchState(state, message)); return; }
        _ = ExecuteScriptAsync("setLaunchState", state, message);
    }

    private void ReportLoginState(string state, string message, string? profileName = null)
    {
        if (!pageReady) return;
        if (InvokeRequired) { BeginInvoke(() => ReportLoginState(state, message, profileName)); return; }
        _ = ExecuteScriptAsync("setAccountLoginState", state, message, profileName ?? "");
    }

    private static string CreateCodeVerifier() => Base64Url(RandomNumberGenerator.GetBytes(64));
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string FormEncode(IEnumerable<KeyValuePair<string, string>> values) => string.Join("&", values.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    private static Dictionary<string, string> ParseQuery(string query) => query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(part => part.Split('=', 2)).ToDictionary(part => Uri.UnescapeDataString(part[0]), part => Uri.UnescapeDataString((part.Length > 1 ? part[1] : "").Replace('+', ' ')));
    private static string FriendlyError(Exception exception)
    {
        if (exception is OperationCanceledException) return "Sign-in timed out. Please try again.";
        if (exception.Message.Contains("Invalid app registration", StringComparison.OrdinalIgnoreCase))
            return "Minecraft authentication rejected this app registration. In Entra, enable public client flow, add Mobile/Desktop redirect http://localhost, allow personal Microsoft accounts, and retry.";
        return exception.Message;
    }
    private static string AccountFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IDIOTCORDLauncher", "account.json");
    private static string Protect(string value) => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    private static string Unprotect(string value) => Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
    private static void SaveAccount(AccountRecord savedAccount) { Directory.CreateDirectory(Path.GetDirectoryName(AccountFilePath)!); File.WriteAllText(AccountFilePath, JsonSerializer.Serialize(savedAccount)); }
    private static AccountRecord? LoadAccount() => File.Exists(AccountFilePath) ? JsonSerializer.Deserialize<AccountRecord>(File.ReadAllText(AccountFilePath)) : null;
    private sealed record MicrosoftToken(string AccessToken, string RefreshToken);
    private sealed record MinecraftLogin(string ProfileName, string ProfileId, string RefreshToken, string AccessToken);
    private sealed record AccountRecord(string ProfileName, string ProfileId, string ProtectedRefreshToken);
}

internal sealed record LauncherSettings(string ClientId, string UpdateFeedUrl = "", string DownloadUrl = "")
{
    public bool IsConfigured => Guid.TryParse(ClientId, out _);
    public static LauncherSettings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "launcher.settings.json");
        if (!File.Exists(path)) return new LauncherSettings("");
        try { return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new LauncherSettings(""); }
        catch (JsonException) { return new LauncherSettings(""); }
    }
}
