namespace ProMapCargo.Api.Services;

/// <summary>
/// SHA-256 of a published file (map, routing package) from its "&lt;file&gt;.sha256" sidecar,
/// written by scripts/write-checksums.ps1 in the sha256sum format. A sidecar older than
/// the file belongs to a previous version and is ignored. The app verifies its download
/// against it.
/// </summary>
public static class FileChecksum
{
    public static string? Read(string path)
    {
        try
        {
            var sidecar = new FileInfo(path + ".sha256");
            if (!sidecar.Exists || sidecar.LastWriteTimeUtc < File.GetLastWriteTimeUtc(path))
            {
                return null;
            }

            var hash = File.ReadAllText(sidecar.FullName).Split(' ', 2)[0].Trim();
            return hash.Length == 64 && hash.All(Uri.IsHexDigit) ? hash.ToLowerInvariant() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
