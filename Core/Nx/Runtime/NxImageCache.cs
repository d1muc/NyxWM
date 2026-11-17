using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;

namespace nyxWM.Nx
{
    /// <summary>
    /// Bitmaps named by an element's "icon" property, which is how a document
    /// uses its own artwork instead of the built-in glyphs.
    ///
    /// A relative path is resolved against the directory of the document that
    /// named it, so a shipped surface can carry a folder of icons beside it.
    /// A missing or unreadable file yields null: the element keeps its place and
    /// draws nothing rather than taking the surface down.
    /// </summary>
    internal static class NxImageCache
    {
        private static readonly string[] Extensions =
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".tif", ".tiff", ".webp"
        };

        private static readonly ConcurrentDictionary<string, Image?> Cache =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>True when an icon value names a file instead of a built-in glyph.</summary>
        internal static bool IsPath(string icon)
        {
            if (string.IsNullOrEmpty(icon))
                return false;

            foreach (string extension in Extensions)
            {
                if (icon.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // "icons/play" and "icons\play" are files too, extension or not.
            return icon.Contains('/') || icon.Contains('\\');
        }

        /// <summary>Loads an icon file, or null when it cannot be read.</summary>
        internal static Image? Get(string icon, string? sourceFile)
        {
            string path = Resolve(icon, sourceFile);

            return Cache.GetOrAdd(path, Load);
        }

        private static string Resolve(string icon, string? sourceFile)
        {
            try
            {
                if (Path.IsPathRooted(icon))
                    return icon;

                string? directory = string.IsNullOrEmpty(sourceFile)
                    ? null
                    : Path.GetDirectoryName(sourceFile);

                if (string.IsNullOrEmpty(directory))
                    return icon;

                return Path.GetFullPath(Path.Combine(directory, icon));
            }
            catch (Exception)
            {
                return icon;
            }
        }

        private static Image? Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Console.WriteLine($"nx: icon file '{path}' not found");
                    return null;
                }

                // Copy the bitmap so the file is not held open: the artist should
                // be able to overwrite an icon while the bar is on screen.
                using var stream = File.OpenRead(path);
                using Image source = Image.FromStream(stream);

                return new Bitmap(source);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"nx: icon file '{path}' failed: {ex.Message}");
                return null;
            }
        }
    }
}
