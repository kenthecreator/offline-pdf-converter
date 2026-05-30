using System.IO;

namespace OfflinePDFConverter.Services;

public static class FileNameHelper
{
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

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
}
