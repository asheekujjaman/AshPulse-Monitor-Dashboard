namespace UniversalITMonitor.Web.Infrastructure;

public static class StoragePathResolver
{
    public static string ResolveDataDirectory(
        string contentRoot,
        string webRoot,
        string? configuredDirectory)
    {
        contentRoot = Path.GetFullPath(contentRoot);
        webRoot = Path.GetFullPath(webRoot);

        var candidate = string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.Combine(contentRoot, "App_Data")
            : Path.IsPathRooted(configuredDirectory)
                ? configuredDirectory
                : Path.Combine(contentRoot, configuredDirectory);
        candidate = Path.GetFullPath(candidate);

        if (IsSameOrChild(webRoot, candidate))
        {
            throw new InvalidOperationException("The JSON data directory must be outside wwwroot.");
        }

        RejectReparsePath(candidate);

        return candidate;
    }

    private static void RejectReparsePath(string path)
    {
        var current = Path.GetPathRoot(path)
            ?? throw new InvalidOperationException("The JSON data directory must be an absolute path.");
        foreach (var segment in path[Path.GetPathRoot(path)!.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Length == 0) continue;
            current = Path.Combine(current, segment);
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("The JSON data directory cannot pass through linked directories.");
        }
    }

    public static bool IsSameOrChild(string root, string candidate)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(candidate));
        return relative == "." || !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}
