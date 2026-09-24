using System.Runtime.InteropServices;
using System.ComponentModel;

namespace OfflinePDFConverter.Services;

/// <summary>Publishes a complete file without replacing an existing output.</summary>
public static class AtomicFile
{
    // Unix rename may replace a destination during a race; link creates it exclusively.
    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(string existingPath, string newPath);

    [DllImport("libc", EntryPoint = "renamex_np", SetLastError = true)]
    private static extern int RenameExclusive(string existingPath, string newPath, uint flags);

    private static void Publish(string temporary, string target)
    {
        if (OperatingSystem.IsWindows()) { File.Move(temporary, target, overwrite: false); return; }
        if (OperatingSystem.IsMacOS())
        {
            if (RenameExclusive(temporary, target, 0x00000004) == 0) return;
            throw new IOException("完成ファイルを確定できませんでした。", new Win32Exception(Marshal.GetLastPInvokeError()));
        }
        if (Link(temporary, target) != 0)
            throw new IOException("完成ファイルを確定できませんでした。", new Win32Exception(Marshal.GetLastPInvokeError()));
        File.Delete(temporary);
    }

    public static string Write(string destination, Action<string> write, CancellationToken token = default)
    {
        destination = Path.GetFullPath(destination);
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.partial{Path.GetExtension(destination)}");
        try
        {
            token.ThrowIfCancellationRequested();
            write(temporary);
            token.ThrowIfCancellationRequested();
            if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0)
                throw new IOException("保存するデータが空でした。");
            for (var attempt = 0; attempt < 10000; attempt++)
            {
                var target = FileNameHelper.GetUniquePath(destination);
                try { Publish(temporary, target); return target; }
                catch (IOException) when (File.Exists(target)) { }
            }
            throw new IOException("出力ファイル名を確保できませんでした。");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
