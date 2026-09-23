using System.Security.Cryptography;
using System.Text.Json;

namespace Scriblism.Core.Installation;

public static class InstallationDiscovery
{
    public static string? FindExisting(string? registeredPath, string defaultPath)
    {
        if (!string.IsNullOrWhiteSpace(registeredPath)) return registeredPath;
        // Recover only the known default location. An arbitrary folder named Scriblism
        // is not enough: it must carry a valid ownership manifest and the matching app.
        try
        {
            if (!File.Exists(Path.Combine(defaultPath, InstallFiles.ManifestName))) return null;
            var manifest = InstallFiles.ReadManifest(defaultPath);
            if (!Version.TryParse(manifest.Version, out _) || !manifest.Files.TryGetValue("Scriblism.exe", out var hash)
                || !manifest.Files.ContainsKey("Scriblism.Setup.exe")) return null;
            using var executable = new FileStream(InstallFiles.Resolve(defaultPath, "Scriblism.exe"),
                FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(executable)).Equals(hash, StringComparison.OrdinalIgnoreCase)
                ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(defaultPath)) : null;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}
