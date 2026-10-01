using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace AppCore
{
    public static class FFNxDeployment
    {
        public const string MarkerName = ".7h-custom-ffnx.json";

        public static bool IsCustomManaged(string gameDirectory, string appDirectory)
        {
            if (File.Exists(Path.Combine(gameDirectory, MarkerName))) return true;
            string staged = Path.Combine(appDirectory, "FFNx");
            return Directory.Exists(staged) && Directory.GetFiles(staged, "*", SearchOption.AllDirectories).Length > 0;
        }

        public static int Apply(string gameDirectory, string appDirectory)
        {
            string staged = Path.Combine(appDirectory, "FFNx");
            if (!Directory.Exists(staged)) return 0;
            string root = Path.GetFullPath(gameDirectory) + Path.DirectorySeparatorChar;
            string marker = Path.Combine(gameDirectory, MarkerName);
            var deployed = File.Exists(marker)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(marker))
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (deployed == null) throw new InvalidDataException("The custom FFNx deployment record is invalid.");
            int changed = 0;
            foreach (string source in Directory.GetFiles(staged, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(staged, source);
                string destination = Path.GetFullPath(Path.Combine(root, relative));
                if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith(".7h-", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid FFNx staged path: " + relative);
                string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
                bool isConfig = relative.Equals("FFNx.toml", StringComparison.OrdinalIgnoreCase);
                if (deployed.TryGetValue(relative, out string previousHash) && previousHash == hash && File.Exists(destination) &&
                    (isConfig || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination))) == hash)) continue;
                if (relative.Equals("FFNx.toml", StringComparison.OrdinalIgnoreCase))
                {
                    var config = Tomlyn.TomlSerializer.Deserialize<Tomlyn.Model.TomlTable>(File.ReadAllText(source));
                    if (config == null || !config.ContainsKey("renderer_backend")) throw new InvalidDataException("Staged FFNx.toml is missing renderer_backend.");
                }
                string backup = Path.Combine(gameDirectory, ".7h-ffnx-original", relative);
                if (!deployed.ContainsKey(relative) && File.Exists(destination) && !File.Exists(backup))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backup));
                    File.Copy(destination, backup);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(source, destination, true);
                deployed[relative] = hash;
                // Record each completed copy so a later failure can be retried.
                File.WriteAllText(marker, JsonSerializer.Serialize(deployed, new JsonSerializerOptions { WriteIndented = true }));
                changed++;
            }
            return changed;
        }
    }
}
