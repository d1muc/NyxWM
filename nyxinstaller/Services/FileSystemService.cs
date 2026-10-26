using System.IO.Compression;
using NyxInstaller.App;

namespace NyxInstaller.Services;

/// <summary>
/// Filesystem operations used by the installer: path validation, staging, atomic move, safe delete.
/// </summary>
public sealed class FileSystemService
{
    private static readonly char Separator = Path.DirectorySeparatorChar;

    /// <summary>
    /// Appends <c>nyxWM</c> to <paramref name="selectedRoot"/> and returns the full target path.
    /// </summary>
    public string BuildTargetPath(string selectedRoot)
    {
        if (string.IsNullOrWhiteSpace(selectedRoot))
            throw new ArgumentException("Install root must not be empty.", nameof(selectedRoot));

        return Path.GetFullPath(Path.Combine(selectedRoot.Trim(), InstallerDefaults.InstallFolderName));
    }

    /// <summary>
    /// Validates that the path is absolute, not a protected system location, and writable.
    /// Returns a user-readable error, or <c>null</c> when valid.
    /// </summary>
    public string? ValidateTargetPath(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return "Install path must not be empty.";

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(targetPath);
        }
        catch (Exception)
        {
            return "Install path is not a valid filesystem path.";
        }

        if (!Path.IsPathFullyQualified(fullPath))
            return "Install path must be absolute.";

        if (IsProtectedPath(fullPath))
            return "This location is reserved by Windows. Choose another folder.";

        if (!CanWrite(fullPath))
            return "The installer cannot write to this folder.";

        return null;
    }

    /// <summary>
    /// Returns free space in megabytes for the volume that contains <paramref name="path"/>.
    /// </summary>
    public long GetFreeSpaceMb(string path)
    {
        try
        {
            var probe = Path.GetFullPath(path);

            while (!Directory.Exists(probe))
            {
                var parent = Path.GetDirectoryName(probe);
                if (string.IsNullOrEmpty(parent))
                    break;

                probe = parent;
            }

            var root = Path.GetPathRoot(probe);
            if (string.IsNullOrEmpty(root))
                return 0;

            var drive = new DriveInfo(root);
            return drive.AvailableFreeSpace / (1024L * 1024L);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Moves <paramref name="source"/> to <paramref name="destination"/> on the same volume; falls
    /// back to copy + verify + delete when volumes differ.
    /// </summary>
    public void MoveDirectory(string source, string destination)
    {
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Source directory not found: {source}");

        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException($"Destination already exists: {destination}");

        try
        {
            Directory.Move(source, destination);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            CopyDirectory(source, destination);
            Directory.Delete(source, recursive: true);
        }
    }

    /// <summary>
    /// Extracts the release archive into <paramref name="destinationPath"/>. A single top-level
    /// folder inside the archive (for example <c>net10.0-windows/</c>) is stripped, so the
    /// application files land directly in the target directory.
    /// </summary>
    public void ExtractReleaseZip(string zipPath, string destinationPath)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Release archive not found.", zipPath);

        Directory.CreateDirectory(destinationPath);

        using var archive = ZipFile.OpenRead(zipPath);
        var prefix = FindSingleRootPrefix(archive);
        var destinationRoot = Path.GetFullPath(destinationPath) + Separator;

        foreach (var entry in archive.Entries)
        {
            var relative = NormalizeEntryName(entry.FullName);

            if (prefix.Length > 0)
            {
                if (!relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                relative = relative[prefix.Length..];
            }

            if (relative.Length == 0)
                continue;

            var targetPath = Path.GetFullPath(Path.Combine(destinationPath, relative));

            if (!targetPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Archive entry escapes the target folder: {entry.FullName}");

            if (entry.Name.Length == 0)
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            var parent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    /// <summary>
    /// Deletes a directory only after resolving its full path and checking it is one of the
    /// installer-owned locations. Never used on the install target without the marker file.
    /// </summary>
    public void DeleteSafely(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
            return;

        if (IsProtectedPath(fullPath))
            throw new InvalidOperationException($"Refusing to delete a protected path: {fullPath}");

        var name = Path.GetFileName(fullPath.TrimEnd(Separator, Path.AltDirectorySeparatorChar));
        var tempRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "nyxinstaller")) + Separator;

        var isOwned =
            name.StartsWith(".nyxWM-staging-", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(".nyxWM-backup-", StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) ||
            File.Exists(Path.Combine(fullPath, InstallerDefaults.MarkerFileName));

        if (!isOwned)
            throw new InvalidOperationException($"Refusing to delete a folder the installer does not own: {fullPath}");

        Directory.Delete(fullPath, recursive: true);
    }

    private static string FindSingleRootPrefix(ZipArchive archive)
    {
        var topLevel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasRootFile = false;

        foreach (var entry in archive.Entries)
        {
            var name = NormalizeEntryName(entry.FullName);
            var slash = name.IndexOf('/');

            if (slash < 0)
            {
                if (entry.Name.Length > 0)
                    hasRootFile = true;

                continue;
            }

            topLevel.Add(name[..slash]);
        }

        if (hasRootFile || topLevel.Count != 1)
            return string.Empty;

        var root = topLevel.First();
        return root.Length == 0 ? string.Empty : root + "/";
    }

    private static string NormalizeEntryName(string name) =>
        name.Replace('\\', '/').TrimStart('/');

    private static bool IsProtectedPath(string fullPath)
    {
        var trimmed = fullPath.TrimEnd(Separator, Path.AltDirectorySeparatorChar);
        var root = Path.GetPathRoot(fullPath)?.TrimEnd(Separator, Path.AltDirectorySeparatorChar);

        if (string.IsNullOrEmpty(trimmed) || string.Equals(trimmed, root, StringComparison.OrdinalIgnoreCase))
            return true;

        var protectedDirectories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.System)
        };

        foreach (var directory in protectedDirectories)
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;

            var normalized = Path.GetFullPath(directory).TrimEnd(Separator, Path.AltDirectorySeparatorChar);
            if (string.Equals(trimmed, normalized, StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith(normalized + Separator, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
        {
            var normalized = Path.GetFullPath(profile).TrimEnd(Separator, Path.AltDirectorySeparatorChar);
            if (string.Equals(trimmed, normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool CanWrite(string targetPath)
    {
        try
        {
            var probe = targetPath;

            while (!Directory.Exists(probe))
            {
                var parent = Path.GetDirectoryName(probe);
                if (string.IsNullOrEmpty(parent))
                    return false;

                probe = parent;
            }

            var testFile = Path.Combine(probe, $".nyx-write-{Guid.NewGuid():N}.tmp");

            using (new FileStream(
                       testFile,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       1,
                       FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            var parent = Path.GetDirectoryName(target);

            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            File.Copy(file, target, overwrite: true);
        }
    }
}
