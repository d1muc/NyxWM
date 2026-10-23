using System.Net.Http;
using System.Net.Http.Headers;
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

    private readonly HttpClient _httpClient;

    public GitHubReleaseService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("nyxinstaller", "0.1.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    /// <summary>
    /// Resolves the latest release and selects the zip asset (plus an optional <c>.sha256</c>).
    /// </summary>
    public Task<ReleaseAsset> GetLatestAsync(CancellationToken cancellationToken)
    {
        // TODO(step 4): GET LatestReleaseUri, parse tag_name and assets, pick by
        // InstallerDefaults.ReleaseAssetPattern, fall back to the exact asset name.
        throw new NotImplementedException();
    }

    /// <summary>
    /// Downloads <paramref name="asset"/> into <paramref name="destinationPath"/> and reports
    /// progress in the 0..1 range.
    /// </summary>
    public Task DownloadAsync(
        ReleaseAsset asset,
        string destinationPath,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        // TODO(step 4): stream to a .part file, rename on success, verify Sha256 when present.
        throw new NotImplementedException();
    }

    public void Dispose() => _httpClient.Dispose();
}
