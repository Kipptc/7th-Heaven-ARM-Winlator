using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace AppCore
{
    /// <summary>
    /// Extracts ordinary files without querying Wine's drive mappings as reparse points.
    /// Rejects archive paths and link entries that could write outside the destination.
    /// </summary>
    public static class SafeZipExtractor
    {
        public static void ExtractFiles(string zipPath, string destinationDirectory)
        {
            string root = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string name = entry.FullName.Replace('\\', '/');
                if (string.IsNullOrEmpty(name) || name.StartsWith('/') ||
                    name.Split('/').Any(part => part == ".." || part == "." || part.Contains(':')))
                    throw new InvalidDataException("Unsafe FFNx ZIP entry: " + entry.FullName);

                // ZIP Unix mode 0120000 denotes a symbolic link. The low Windows attributes
                // can also mark a reparse point; neither belongs in the FFNx release ZIP.
                int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
                if (unixType == 0xA000 || (entry.ExternalAttributes & 0x400) != 0)
                    throw new InvalidDataException("FFNx ZIP contains a link: " + entry.FullName);

                string target = Path.GetFullPath(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("FFNx ZIP entry escapes the game folder: " + entry.FullName);

                if (name.EndsWith('/')) continue;

                string directory = Path.GetDirectoryName(target) ?? throw new InvalidDataException("Invalid FFNx ZIP entry: " + entry.FullName);
                Directory.CreateDirectory(directory);
                using Stream source = entry.Open();
                using FileStream output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                source.CopyTo(output);
            }
        }
    }
}
