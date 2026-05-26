using System.IO;
using OfflinePDFImageConverter.Models;
using PDFtoImage;
using SkiaSharp;

namespace OfflinePDFImageConverter.Services;

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

        Directory.CreateDirectory(request.OutputFolder);

        var pageCounts = new Dictionary<string, int>();
        var errors = new List<string>();
        var totalPages = 0;

        foreach (var pdfPath in request.PdfFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var stream = File.OpenRead(pdfPath);
                var pageCount = Conversion.GetPageCount(stream);
                pageCounts[pdfPath] = pageCount;
                totalPages += pageCount;
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

        foreach (var pdfPath in request.PdfFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!pageCounts.TryGetValue(pdfPath, out var pageCount))
            {
                continue;
            }

            try
            {
                var digits = Math.Max(3, pageCount.ToString().Length);
                var baseName = FileNameHelper.SafeBaseName(pdfPath);
                var extension = request.OutputFormat == PdfImageFormat.Png ? "png" : "jpg";
                var format = request.OutputFormat == PdfImageFormat.Png
                    ? SKEncodedImageFormat.Png
                    : SKEncodedImageFormat.Jpeg;

                using var stream = File.OpenRead(pdfPath);
                var options = new RenderOptions(
                    Dpi: request.Dpi,
                    WithAnnotations: true,
                    BackgroundColor: SKColors.White,
                    UseTiling: true);

                var pageIndex = 0;
                foreach (var bitmap in Conversion.ToImages(stream, options: options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using (bitmap)
                    {
                        var pageNumber = (pageIndex + 1).ToString($"D{digits}");
                        var desiredPath = Path.Combine(request.OutputFolder, $"{baseName}_page{pageNumber}.{extension}");
                        var outputPath = FileNameHelper.GetUniquePath(desiredPath);

                        using var output = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
                        bitmap.Encode(output, format, request.OutputFormat == PdfImageFormat.Png ? 100 : 95);
                        createdFiles++;
                    }

                    pageIndex++;
                    completed++;
                    progress.Report(new ConversionProgress(
                        completed,
                        totalPages,
                        $"{Path.GetFileName(pdfPath)}: {pageIndex}/{pageCount}ページを保存しました"));
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
}

#pragma warning restore CA1416
