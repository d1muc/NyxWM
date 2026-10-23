namespace NyxInstaller.Models;

/// <summary>
/// Downloadable file attached to a GitHub release.
/// </summary>
/// <param name="Name">Asset file name.</param>
/// <param name="DownloadUrl">Browser download URL.</param>
/// <param name="SizeBytes">Asset size reported by the API.</param>
/// <param name="Version">Release tag the asset belongs to.</param>
/// <param name="Sha256">Optional checksum, when a <c>.sha256</c> asset is published.</param>
public sealed record ReleaseAsset(
    string Name,
    string DownloadUrl,
    long SizeBytes,
    string Version,
    string? Sha256);
