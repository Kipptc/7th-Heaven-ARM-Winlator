/*
  This source is subject to the Microsoft Public License. See LICENSE.TXT for details.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Iros.Workshop
{
    internal static class Native7zExtractor
    {
        private const string ExtractorFileName = "7zr-arm64.exe";

        internal static bool Is7zArchive(ReadOnlySpan<byte> signature) =>
            signature.Length >= 6 && signature[0] == 0x37 && signature[1] == 0x7A &&
            signature[2] == 0xBC && signature[3] == 0xAF && signature[4] == 0x27 && signature[5] == 0x1C;

        internal static bool Extract(string source, string destination, string extractInto)
        {
            string extractor = Path.Combine(AppContext.BaseDirectory, ExtractorFileName);
            if (!File.Exists(extractor))
                throw new FileNotFoundException("The Bannerlator ARM64 7z extractor is missing.", extractor);

            string scratch = Path.Combine(Path.GetDirectoryName(source)!, "7h-extract-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            try
            {
                var start = new ProcessStartInfo(extractor)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };
                start.ArgumentList.Add("x");
                start.ArgumentList.Add("-y");
                start.ArgumentList.Add("-bd");
                start.ArgumentList.Add("-bso0");
                start.ArgumentList.Add("-bsp0");
                start.ArgumentList.Add("-bse1");
                start.ArgumentList.Add("-o" + scratch);
                start.ArgumentList.Add(source);

                using var process = Process.Start(start) ?? throw new IOException("Could not start the ARM64 7z extractor.");
                if (!process.WaitForExit(900000))
                {
                    process.Kill(entireProcessTree: true);
                    throw new TimeoutException("The ARM64 7z extractor did not finish within 15 minutes.");
                }
                string error = process.StandardError.ReadToEnd().Trim();
                if (process.ExitCode != 0)
                    throw new InvalidDataException($"The ARM64 7z extractor exited with code {process.ExitCode}: {error}");

                RejectLinks(scratch);

                // Catalog archives with an IRO have the same first-IRO behavior as
                // the managed extractor. Move it within the library drive without
                // allocating another large managed buffer.
                string iro = Directory.EnumerateFiles(scratch, "*", SearchOption.AllDirectories)
                    .FirstOrDefault(path => Path.GetExtension(path).Equals(".iro", StringComparison.OrdinalIgnoreCase));
                if (iro != null)
                {
                    File.Move(iro, destination, overwrite: true);
                    return true;
                }

                string root = destination.EndsWith(".iro", StringComparison.OrdinalIgnoreCase)
                    ? destination.Substring(0, destination.Length - 4) : destination;
                string targetRoot = string.IsNullOrWhiteSpace(extractInto) ? root : Path.Combine(root, extractInto);
                targetRoot = Path.GetFullPath(targetRoot);
                string rootPrefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!targetRoot.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) &&
                    !targetRoot.Equals(rootPrefix.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The mod's ExtractInto path escapes its install folder.");

                Directory.CreateDirectory(targetRoot);
                foreach (string file in Directory.EnumerateFiles(scratch, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(scratch, file);
                    string target = Path.GetFullPath(Path.Combine(targetRoot, relative));
                    if (!target.StartsWith(targetRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("An archive entry escapes its install folder: " + relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Move(file, target, overwrite: true);
                }
                return false;
            }
            finally
            {
                try { Directory.Delete(scratch, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static void RejectLinks(string root)
        {
            var directories = new Stack<string>();
            directories.Push(root);
            while (directories.Count > 0)
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(directories.Pop()))
                {
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("A mod archive contains a link: " + entry);
                    if ((attributes & FileAttributes.Directory) != 0)
                        directories.Push(entry);
                }
            }
        }
    }
}
