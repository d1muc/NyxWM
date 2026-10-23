namespace NyxInstaller.Services;

/// <summary>
/// Filesystem operations used by the installer: path validation, staging, atomic move, safe delete.
/// </summary>
public sealed class FileSystemService
{
    /// <summary>
    /// Appends <c>nyxWM</c> to <paramref name="selectedRoot"/> and returns the full target path.
    /// </summary>
    public string BuildTargetPath(string selectedRoot)
    {
        // TODO(step 3): Path.GetFullPath + InstallerDefaults.InstallFolderName.
        throw new NotImplementedException();
    }

    /// <summary>
    /// Validates that the path is absolute, not a protected system location, and writable.
    /// Returns a user-readable error, or <c>null</c> when valid.
    /// </summary>
    public string? ValidateTargetPath(string targetPath)
    {
        // TODO(step 3): reject drive roots, Windows, Program Files, user profile root; test write access.
        throw new NotImplementedException();
    }

    /// <summary>
    /// Returns free space in megabytes for the volume that contains <paramref name="path"/>.
    /// </summary>
    public long GetFreeSpaceMb(string path)
    {
        // TODO(step 3): DriveInfo.
        throw new NotImplementedException();
    }

    /// <summary>
    /// Moves <paramref name="source"/> to <paramref name="destination"/> on the same volume; falls
    /// back to copy + verify + delete when volumes differ.
    /// </summary>
    public void MoveDirectory(string source, string destination)
    {
        // TODO(step 6).
        throw new NotImplementedException();
    }

    /// <summary>
    /// Deletes a directory only after resolving its full path and checking it is one of the
    /// installer-owned locations. Never used on the install target without the marker file.
    /// </summary>
    public void DeleteSafely(string path)
    {
        // TODO(step 6): Path.GetFullPath, safety checks, then Directory.Delete(recursive: true).
        throw new NotImplementedException();
    }
}
