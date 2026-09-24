using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace OfflinePDFConverter.Services;

/// <summary>Owns one private extraction of the OCR files embedded in the application.</summary>
public sealed class BundledOcrRuntime : IDisposable
{
    private static readonly object Gate = new();
    private static BundledOcrRuntime? current;
    public string RootDirectory { get; }
    public string EnginePath => Path.Combine(RootDirectory, "tesseract.exe");
    public string DataDirectory => Path.Combine(RootDirectory, "tessdata");
    private BundledOcrRuntime(string root) => RootDirectory = root;

    public static BundledOcrRuntime Get(CancellationToken token = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("このv3.2.0の内蔵OCRはWindows版に対応しています。");
        lock (Gate)
        {
            token.ThrowIfCancellationRequested();
            return current ??= ExtractEmbedded(token);
        }
    }

    public static BundledOcrRuntime ExtractEmbedded(CancellationToken token = default)
    {
        var assembly = typeof(BundledOcrRuntime).Assembly;
        using var payload = assembly.GetManifestResourceStream("OfflinePDFConverter.WindowsOcr.zip")
            ?? throw new InvalidDataException("アプリ内の文字認識部品が見つかりません。配布元のexeを取得し直してください。");
        using var hashStream = assembly.GetManifestResourceStream("OfflinePDFConverter.WindowsOcr.sha256")
            ?? throw new InvalidDataException("文字認識部品の確認情報がありません。");
        using var reader = new StreamReader(hashStream);
        return Extract(payload, reader.ReadToEnd().Trim(), token);
    }

    public static BundledOcrRuntime Extract(Stream payload, string expectedHash, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!payload.CanSeek || payload.Length > 128 * 1024 * 1024)
            throw new InvalidDataException("文字認識部品の形式が正しくありません。");
        if (!Convert.ToHexString(SHA256.HashData(payload)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("アプリ内の文字認識部品が破損しています。配布元のexeを取得し直してください。");
        payload.Position = 0;
        using var zip = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: true);
        var manifestEntry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("文字認識部品の一覧がありません。");
        if (manifestEntry.Length > 1024 * 1024) throw new InvalidDataException("部品一覧が大きすぎます。");
        using var manifestStream = manifestEntry.Open();
        var files = JsonSerializer.Deserialize<Dictionary<string, string>>(manifestStream)
            ?? throw new InvalidDataException("文字認識部品の一覧が不正です。");
        var entries = zip.Entries.Where(x => x.FullName != "manifest.json").ToArray();
        if (entries.Length != files.Count || entries.Select(x => x.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
            throw new InvalidDataException("文字認識部品の一覧が一致しません。");
        foreach (var required in new[] { "tesseract.exe", "tessdata/jpn.traineddata", "tessdata/jpn_vert.traineddata", "tessdata/eng.traineddata" })
            if (!files.ContainsKey(required)) throw new InvalidDataException("必要な文字認識部品が不足しています。");

        var root = Directory.CreateTempSubdirectory("OfflinePDFConverter-3.2.0-OCR-").FullName;
        try
        {
            long total = 0;
            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                var parts = entry.FullName.Split('/', '\\');
                if (parts.Any(x => string.IsNullOrWhiteSpace(x) || x is "." or ".." || x.Contains(':'))
                    || entry.Length > 64 * 1024 * 1024 || (total += entry.Length) > 128 * 1024 * 1024
                    || !files.TryGetValue(entry.FullName, out var hash))
                    throw new InvalidDataException("文字認識部品の保存先またはサイズが不正です。");
                var destination = Path.Combine(root, Path.Combine(parts));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using (var input = entry.Open())
                using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    long copied = 0;
                    int read;
                    while ((read = input.Read(buffer)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        copied += read;
                        if (copied > entry.Length) throw new InvalidDataException("文字認識部品のサイズが一致しません。");
                        output.Write(buffer, 0, read);
                    }
                    if (copied != entry.Length) throw new InvalidDataException("文字認識部品が途中で切れています。");
                }
                using var saved = File.OpenRead(destination);
                if (!Convert.ToHexString(SHA256.HashData(saved)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("文字認識部品の検証に失敗しました。");
            }
            token.ThrowIfCancellationRequested();
            return new BundledOcrRuntime(root);
        }
        catch { Directory.Delete(root, recursive: true); throw; }
    }

    public static void Cleanup()
    {
        lock (Gate) { current?.Dispose(); current = null; }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, recursive: true); }
        catch (IOException) { } // A forced child shutdown or antivirus scan may retain a file temporarily.
        catch (UnauthorizedAccessException) { }
    }
}
