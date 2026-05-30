using System.IO;

namespace OfflinePDFConverter.Models;

public sealed class FileItem
{
    public FileItem(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public string DisplayName => System.IO.Path.GetFileName(Path);

    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
}
