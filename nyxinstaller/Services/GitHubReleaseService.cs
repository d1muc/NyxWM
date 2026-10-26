using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using NyxInstaller.App;
using NyxInstaller.Models;

namespace NyxInstaller.Services;

/// <summary>
/// Talks to the GitHub Releases API and downloads the release asset.
/// </summary>
public sealed class GitHubReleaseService : IDisposable
{
    private static readonly Uri LatestReleaseUri = new(
        $"https://api.github.com/repos/{InstallerDefaults.RepositoryOwner}/{InstallerDefaults.RepositoryName}/releases/latest");

    private const int BufferSize = 81920;

    private readonly HttpClient _httpClient;

    public GitHubReleaseService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("nyxinstaller", "0.2.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    /// <summary>
    /// Resolves the latest release and selects the zip asset (plus an optional <c>.sha256</c>).
    /// </summary>
    public async Task<ReleaseAsset> GetLatestAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync(LatestReleaseUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("No nyxWM release is published yet.");

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        using var document = await JsonDocument
            .ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var root = document.RootElement;
        var version = root.TryGetProperty("tag_name", out var tag)
            ? tag.GetString() ?? string.Empty
            : string.Empty;

        var assets = ReadAssets(root);
        var zip = SelectZip(assets)
                  ?? throw new InvalidOperationException(
                      "The latest release has no zip asset.");

        var checksumName = zip.Name + ".sha256";
        var checksumAsset = assets.FirstOrDefault(asset =>
            asset.Name.Equals(checksumName, StringComparison.OrdinalIgnoreCase) ||
            asset.Name.Equals(
                Path.GetFileNameWithoutExtension(zip.Name) + ".sha256",
                StringComparison.OrdinalIgnoreCase));

        var sha256 = checksumAsset is null
            ? null
            : await TryReadChecksumAsync(checksumAsset.DownloadUrl, cancellationToken).ConfigureAwait(false);

        return new ReleaseAsset(zip.Name, zip.DownloadUrl, zip.SizeBytes, version, sha256);
    }

    /// <summary>
    /// Downloads <paramref name="asset"/> into <paramref name="destinationPath"/> and reports
    /// progress in the 0..1 range.
    /// </summary>
    public async Task DownloadAsync(
        ReleaseAsset asset,
        string destinationPath,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var partialPath = destinationPath + ".part";

        try
        {
            using var response = await _httpClient
                .GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? asset.SizeBytes;

            await using var input = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            await using (var output = new FileStream(
                             partialPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             BufferSize,
                             useAsync: true))
            {
                var buffer = new byte[BufferSize];
                long received = 0;
                int read;

                while ((read = await input.ReadAsync(buffer.AsMemory(), cancellationToken)
                           .ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);

                    received += read;

                    if (totalBytes > 0)
                    {
                        progress.Report(Math.Clamp(
                            (double)received / totalBytes,
                            0d,
                            1d));
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(asset.Sha256))
            {
                var actual = await ComputeSha256Async(partialPath, cancellationToken)
                    .ConfigureAwait(false);

                if (!actual.Equals(asset.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Checksum mismatch for {asset.Name}. Expected {asset.Sha256}, got {actual}.");
                }
            }

            File.Move(partialPath, destinationPath, overwrite: true);
            progress.Report(1d);
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }
    }

    private static List<AssetInfo> ReadAssets(JsonElement root)
    {
        var assets = new List<AssetInfo>();

        if (!root.TryGetProperty("assets", out var array) || array.ValueKind != JsonValueKind.Array)
            return assets;

        foreach (var item in array.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;

            var url = item.TryGetProperty("browser_download_url", out var urlElement)
                ? urlElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                continue;

            var size = item.TryGetProperty("size", out var sizeElement) &&
                       sizeElement.TryGetInt64(out var value)
                ? value
                : 0L;

            assets.Add(new AssetInfo(name, url, size));
        }

        return assets;
    }

    private static AssetInfo? SelectZip(IReadOnlyList<AssetInfo> assets)
    {
        var exact = assets.FirstOrDefault(asset =>
            asset.Name.Equals(InstallerDefaults.ReleaseAssetName, StringComparison.OrdinalIgnoreCase));

        if (exact != null)
            return exact;

        var patterned = assets.FirstOrDefault(asset => MatchesReleasePattern(asset.Name));
        if (patterned != null)
            return patterned;

        return assets.FirstOrDefault(asset =>
            asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesReleasePattern(string name) =>
        name.StartsWith("nyxWM-", StringComparison.OrdinalIgnoreCase) &&
        name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase);

    private async Task<string?> TryReadChecksumAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var text = await _httpClient
                .GetStringAsync(url, cancellationToken)
                .ConfigureAwait(false);

            var token = text
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            return token;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Cleanup only.
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed record AssetInfo(string Name, string DownloadUrl, long SizeBytes);
}
