using System.IO;
using OfflinePDFConverter.Models;
using PDFtoImage;
using SkiaSharp;

namespace OfflinePDFConverter.Services;

#pragma warning disable CA1416 // PDFtoImage supports Windows/macOS/Linux; this desktop app is published for Windows x64.

public sealed class PdfToImageService : IPdfToImageService
{
    public Task<ConversionResult> ConvertAsync(
        PdfToImageRequest request,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => Convert(request, progress, cancellationToken), cancellationToken);
    }

    private static ConversionResult Convert(
        PdfToImageRequest request,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken)
    {
        Validate(request);
        if (request.JpegQuality is < 1 or > 100) throw new ArgumentException("JPEG品質は1から100で指定してください。");

        Directory.CreateDirectory(request.OutputFolder);

        var pagesByPdf = new Dictionary<string, (int PageCount, IReadOnlyList<int> Pages)>();
        var errors = new List<string>();
        var totalPages = 0;

        foreach (var pdfPath in request.PdfFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var stream = File.OpenRead(pdfPath);
                var password = GetPassword(request.Passwords, pdfPath);
                var pageCount = Conversion.GetPageCount(stream, password: NullIfEmpty(password));
                var pages = GetPagesToConvert(request.PagesToConvert, pageCount);
                pagesByPdf[pdfPath] = (pageCount, pages);
                totalPages += pages.Count;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(pdfPath)}: {FriendlyErrorFormatter.ToUserMessage(ex)}");
            }
        }

        var completed = 0;
        var createdFiles = 0;
        progress.Report(new ConversionProgress(0, totalPages, "変換を開始しています..."));

        for (var pdfFileIndex = 0; pdfFileIndex < request.PdfFiles.Count; pdfFileIndex++)
        {
            var pdfPath = request.PdfFiles[pdfFileIndex];
            cancellationToken.ThrowIfCancellationRequested();
            if (!pagesByPdf.TryGetValue(pdfPath, out var pdfSelection))
            {
                continue;
            }

            try
            {
                var pagesToConvert = pdfSelection.Pages;
                var digits = Math.Max(3, pdfSelection.PageCount.ToString().Length);
                var baseName = GetOutputBaseName(request.OutputBaseName, pdfPath, request.PdfFiles.Count, pdfFileIndex);
                var extension = request.OutputFormat == PdfImageFormat.Png ? "png" : "jpg";
                var format = request.OutputFormat == PdfImageFormat.Png
                    ? SKEncodedImageFormat.Png
                    : SKEncodedImageFormat.Jpeg;

                using var stream = File.OpenRead(pdfPath);
                var password = GetPassword(request.Passwords, pdfPath);
                var options = new RenderOptions(
                    Dpi: request.Dpi,
                    WithAnnotations: true,
                    BackgroundColor: SKColors.White,
                    UseTiling: true);

                var zeroBasedPages = pagesToConvert.Select(page => page - 1).ToArray();
                var convertedIndex = 0;
                foreach (var bitmap in Conversion.ToImages(
                             stream,
                             zeroBasedPages,
                             password: NullIfEmpty(password),
                             options: options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var originalPageNumber = pagesToConvert[convertedIndex];
                    using (bitmap)
                    {
                        var pageNumber = originalPageNumber.ToString($"D{digits}");
                        var desiredPath = Path.Combine(request.OutputFolder, $"{baseName}_page{pageNumber}.{extension}");
                        var outputPath = FileNameHelper.GetUniquePath(desiredPath);

                        AtomicFile.Write(outputPath, path =>
                        {
                            using var output = File.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                            bitmap.Encode(output, format, request.OutputFormat == PdfImageFormat.Png ? 100 : request.JpegQuality);
                        }, cancellationToken);
                        createdFiles++;
                    }

                    convertedIndex++;
                    completed++;
                    progress.Report(new ConversionProgress(
                        completed,
                        totalPages,
                        $"{Path.GetFileName(pdfPath)}: {originalPageNumber}ページ目を保存しました"));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(pdfPath)}: {FriendlyErrorFormatter.ToUserMessage(ex)}");
            }
        }

        return new ConversionResult(createdFiles, errors);
    }

    private static IReadOnlyList<int> GetPagesToConvert(string value, int pageCount)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Enumerable.Range(1, pageCount).ToArray();
        }

        var pages = PageRangeParser.Parse(value, pageCount, "変換する");
        if (pages.Count == 0)
        {
            throw new ArgumentException("変換するページを入力してください。例: 1,3,5-7");
        }

        return pages.ToArray();
    }

    private static string? NullIfEmpty(string value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string GetPassword(IReadOnlyDictionary<string, string> passwords, string pdfPath)
    {
        return passwords.TryGetValue(pdfPath, out var password) ? password : string.Empty;
    }

    private static void Validate(PdfToImageRequest request)
    {
        if (request.PdfFiles.Count == 0)
        {
            throw new ArgumentException("PDFファイルを選択してください。");
        }

        if (string.IsNullOrWhiteSpace(request.OutputFolder))
        {
            throw new ArgumentException("出力先フォルダを選択してください。");
        }

        if (request.Dpi is not (150 or 200 or 300 or 400 or 600))
        {
            throw new ArgumentException("解像度は150、200、300、400、600dpiから選択してください。");
        }

        foreach (var pdfFile in request.PdfFiles)
        {
            if (!File.Exists(pdfFile))
            {
                throw new FileNotFoundException("PDFファイルが見つかりません。", pdfFile);
            }

            if (!string.Equals(Path.GetExtension(pdfFile), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("PDFファイルだけを選択してください。");
            }
        }
    }

    private static string GetOutputBaseName(string requestedBaseName, string pdfPath, int pdfCount, int pdfIndex)
    {
        var baseName = FileNameHelper.BuildOutputBaseName([pdfPath], requestedBaseName);

        return pdfCount <= 1 ? baseName : $"{baseName}_pdf{pdfIndex + 1:D3}";
    }
}

#pragma warning restore CA1416
