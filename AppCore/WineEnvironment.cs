using System;
using System.IO;
using System.Runtime.InteropServices;

namespace AppCore
{
    public static class WineEnvironment
    {
        public static bool ExportFFNxLogIfNewer(string ff7Exe)
        {
            if (string.IsNullOrWhiteSpace(ff7Exe)) return false;
            string gameDirectory = Path.GetDirectoryName(ff7Exe);
            if (string.IsNullOrWhiteSpace(gameDirectory)) return false;
            string logDirectory = TryGetBannerlatorLogDirectory();
            if (logDirectory == null) return false;

            string source = Path.Combine(gameDirectory, "FFNx.log");
            if (!File.Exists(source)) return false;
            string destination = Path.Combine(logDirectory, "FFNx.log");
            if (File.Exists(destination) && File.GetLastWriteTimeUtc(source) <= File.GetLastWriteTimeUtc(destination)) return false;
            File.Copy(source, destination, true);
            return true;
        }

        public static string TryGetBannerlatorLogDirectory()
        {
            if (!IsRunningInWine()) return null;

            string root = @"D:\7h-ARM\logs";
            if (!Directory.Exists(@"D:\")) return null;
            try
            {
                Directory.CreateDirectory(root);
                return root;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        public static bool IsRunningInWine()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WINELOADER")) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WINEPREFIX")))
            {
                return true;
            }

            // Wine exposes this export even when the launcher does not pass Wine variables
            // through to the Windows process (as can happen with compatibility layers).
            if (NativeLibrary.TryLoad("ntdll.dll", out IntPtr ntdll))
            {
                try
                {
                    return NativeLibrary.TryGetExport(ntdll, "wine_get_version", out _);
                }
                finally
                {
                    NativeLibrary.Free(ntdll);
                }
            }

            return false;
        }
    }
}
