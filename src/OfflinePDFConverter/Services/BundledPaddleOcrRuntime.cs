#if PADDLE_OCR
using System.Security.Cryptography;
using System.Text.Json;

namespace OfflinePDFConverter.Services;

/// <summary>Extracts verified embedded models into a private temporary directory.</summary>
public sealed class BundledPaddleOcrRuntime : IDisposable
{
    private const string Prefix = "OfflinePDFConverter.Paddle.";
    public string RootDirectory { get; }
    private BundledPaddleOcrRuntime(string root) => RootDirectory = root;

    public static BundledPaddleOcrRuntime ExtractEmbedded(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        using var manifest = Resource("manifest.json");
        using var document = JsonDocument.Parse(manifest);
        var entries = document.RootElement.GetProperty("files").EnumerateArray().ToArray();
        foreach (var required in new[] { "det.onnx", "rec.onnx", "cls.onnx", "keys.txt", "rec-jpn.onnx", "keys-jpn.txt", "rec-eng.onnx", "keys-eng.txt", "NOTICE.txt" })
            if (!entries.Any(e => e.GetProperty("file").GetString() == required))
                throw new InvalidDataException("PaddleOCRの必要な部品が不足しています。");
        var root = Directory.CreateTempSubdirectory("OfflinePDFConverter-PaddleOCR-").FullName;
        try
        {
            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                var name = entry.GetProperty("file").GetString()!;
                if (Path.GetFileName(name) != name || name is "." or ".." || name.Contains(':') || name.Contains('\\'))
                    throw new InvalidDataException("PaddleOCRの部品一覧が不正です。");
                var expectedBytes = entry.GetProperty("bytes").GetInt64();
                if (expectedBytes is <= 0 or > 32 * 1024 * 1024) throw new InvalidDataException("PaddleOCRの部品サイズが不正です。");
                var destination = Path.Combine(root, name);
                using (var input = Resource(name))
                using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    long copied = 0;
                    int count;
                    while ((count = input.Read(buffer)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        copied += count;
                        if (copied > expectedBytes) throw new InvalidDataException("PaddleOCRの部品サイズが一致しません。");
                        output.Write(buffer, 0, count);
                    }
                    if (copied != expectedBytes) throw new InvalidDataException("PaddleOCRの部品が途中で切れています。");
                }
                using var saved = File.OpenRead(destination);
                if (!Convert.ToHexString(SHA256.HashData(saved)).Equals(entry.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("PaddleOCRの部品が破損しています。");
            }
            token.ThrowIfCancellationRequested();
            return new(root);
        }
        catch { Directory.Delete(root, true); throw; }
    }

    public static string ReadLicenses()
    {
        var assembly = typeof(BundledPaddleOcrRuntime).Assembly;
        return string.Join("\n\n", assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(Prefix) && (name.EndsWith("LICENSE.txt") || name.EndsWith("NOTICE.txt") || name.EndsWith("Notices.txt")))
            .OrderBy(name => name).Select(name =>
            {
                using var resource = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(resource);
                return name[Prefix.Length..] + "\n\n" + reader.ReadToEnd();
            }));
    }

    private static Stream Resource(string name) => typeof(BundledPaddleOcrRuntime).Assembly.GetManifestResourceStream(Prefix + name)
        ?? throw new InvalidDataException("アプリ内のPaddleOCR部品が見つかりません。配布元のアプリを取得し直してください。");

    public void Dispose()
    {
        try { if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
#endif
