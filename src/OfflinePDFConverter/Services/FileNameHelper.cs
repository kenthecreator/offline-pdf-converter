using System.IO;

namespace OfflinePDFConverter.Services;

public static class FileNameHelper
{
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();
    private const int MaximumSourcePartLength = 24;

    public static string SafeBaseName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        foreach (var invalid in InvalidFileNameChars)
        {
            name = name.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "file" : name;
    }

    public static string GetUniquePath(string desiredPath)
    {
        if (!File.Exists(desiredPath))
        {
            return desiredPath;
        }

        var directory = Path.GetDirectoryName(desiredPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(desiredPath);
        var extension = Path.GetExtension(desiredPath);

        for (var i = 2; i < 10000; i++)
        {
            var candidate = Path.Combine(directory, $"{name}_{i}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{name}_{DateTime.Now:yyyyMMddHHmmssfff}{extension}");
    }

    public static string BuildOutputBaseName(
        IEnumerable<string> sourcePaths,
        string? requestedBaseName)
    {
        var sourceLabel = BuildSourceLabel(sourcePaths);
        var requested = string.IsNullOrWhiteSpace(requestedBaseName)
            ? string.Empty
            : SafeBaseName(requestedBaseName);

        if (string.IsNullOrWhiteSpace(sourceLabel))
        {
            return string.IsNullOrWhiteSpace(requested) ? "output" : requested;
        }

        if (string.IsNullOrWhiteSpace(requested)
            || requested.Contains(sourceLabel, StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(requested) ? sourceLabel : requested;
        }

        return $"{sourceLabel}_{requested}";
    }

    public static string IncludeSourceNamesInPath(
        string desiredPath,
        IEnumerable<string> sourcePaths)
    {
        if (string.IsNullOrWhiteSpace(desiredPath))
        {
            return desiredPath;
        }

        var directory = Path.GetDirectoryName(desiredPath) ?? string.Empty;
        var extension = Path.GetExtension(desiredPath);
        var outputBaseName = BuildOutputBaseName(
            sourcePaths,
            Path.GetFileNameWithoutExtension(desiredPath));
        return Path.Combine(directory, $"{outputBaseName}{extension}");
    }

    private static string BuildSourceLabel(IEnumerable<string> sourcePaths)
    {
        var sourceNames = sourcePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(SafeBaseName)
            .Select(TrimSourcePart)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return sourceNames.Count switch
        {
            0 => string.Empty,
            <= 3 => string.Join("_", sourceNames),
            _ => $"{sourceNames[0]}_{sourceNames[1]}_ほか{sourceNames.Count - 2}件"
        };
    }

    private static string TrimSourcePart(string sourceName)
    {
        return sourceName.Length <= MaximumSourcePartLength
            ? sourceName
            : sourceName[..MaximumSourcePartLength].TrimEnd();
    }
}
