using System.IO.Compression;
using System.Xml;
using OfflinePDFConverter.Models;

namespace OfflinePDFConverter.Services;

internal static class OfficePdfService
{
    internal static IReadOnlyList<string> Extensions { get; } = Array.AsReadOnly(new[] { ".docx", ".xlsx", ".pptx" });

    private static readonly Lazy<OfficeKitRuntime> Runtime = new(() => new OfficeKitRuntime());

    internal static void Cleanup()
    {
        if (Runtime.IsValueCreated)
            Runtime.Value.Dispose();
    }

    public static bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static Task<ConversionResult> ConvertAsync(IReadOnlyList<string> files, string? outputFolder,
        IProgress<ConversionProgress> progress, CancellationToken token) => Task.Run(() =>
    {
        var outputs = new List<string>();
        var errors = new List<string>();
        for (var i = 0; i < files.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var source = files[i];
            progress.Report(new ConversionProgress(i, files.Count, $"変換中 · {Path.GetFileName(source)}"));
            try
            {
                outputs.Add(ConvertFile(source, outputFolder, token));
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                errors.Add($"{Path.GetFileName(source)} · {ex.Message}");
            }
        }
        return new ConversionResult(outputs.Count, errors) { OutputPaths = outputs };
    }, token);

    private static string ConvertFile(string source, string? outputFolder, CancellationToken token)
    {
        var extension = Path.GetExtension(source).ToLowerInvariant();
        using var input = File.OpenRead(source);
        ValidatePackage(input, extension);
        token.ThrowIfCancellationRequested();
        var output = Path.ChangeExtension(source, ".pdf");
        if (!string.IsNullOrEmpty(outputFolder))
            output = Path.Combine(outputFolder, Path.GetFileName(output));
        return AtomicFile.Write(output, destination =>
        {
            try
            {
                using var outputStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException("保存先フォルダーにPDFを書き込めません。", ex);
            }

            Runtime.Value.SavePdf(source, destination, token);
        }, token);
    }

    private static void ValidatePackage(Stream input, string extension)
    {
        const string message = "Officeファイルとして開けません。形式とパスワード保護の有無を確認してください。";
        var expectedType = extension switch
        {
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml",
            _ => throw new ArgumentException("対応形式は .docx、.xlsx、.pptx です。")
        };

        try
        {
            using var package = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            var entry = package.GetEntry("[Content_Types].xml") ?? throw new InvalidDataException(message);
            using var stream = entry.Open();
            using var types = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                MaxCharactersInDocument = 1_000_000
            });
            while (types.Read())
            {
                if (types.NodeType != XmlNodeType.Element || types.LocalName != "Override"
                    || types.GetAttribute("ContentType") != expectedType)
                    continue;

                var part = types.GetAttribute("PartName")?.TrimStart('/');
                if (part != null && package.GetEntry(part) != null)
                    return;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException)
        {
            throw new InvalidDataException(message, ex);
        }

        throw new InvalidDataException(message);
    }
}
