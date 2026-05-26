using System.IO;
using OfflinePDFImageConverter.Models;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace OfflinePDFImageConverter.Services;

public sealed class ImageToPdfService : IImageToPdfService
{
    private const double MarginPoints = 36;

    public Task<ConversionResult> ConvertAsync(
        ImageToPdfRequest request,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => Convert(request, progress, cancellationToken), cancellationToken);
    }

    private static ConversionResult Convert(
        ImageToPdfRequest request,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken)
    {
        Validate(request);

        var outputDirectory = Path.GetDirectoryName(request.OutputPdfPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var createdPages = 0;
        var errors = new List<string>();

        using var document = new PdfDocument();
        document.Info.Title = Path.GetFileNameWithoutExtension(request.OutputPdfPath);
        document.Info.Creator = "OfflinePDFImageConverter";

        for (var i = 0; i < request.ImageFiles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var imagePath = request.ImageFiles[i];

            try
            {
                using var image = XImage.FromFile(imagePath);
                var page = document.AddPage();
                ConfigurePage(page, image, request.PageMode, request.UseMargin);

                using var graphics = XGraphics.FromPdfPage(page);
                DrawImage(graphics, page, image, request.UseMargin);

                createdPages++;
                progress.Report(new ConversionProgress(
                    createdPages,
                    request.ImageFiles.Count,
                    $"{Path.GetFileName(imagePath)} をPDFに追加しました"));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(imagePath)}: {FriendlyErrorFormatter.ToUserMessage(ex)}");
            }
        }

        if (createdPages == 0)
        {
            throw new ArgumentException("PDFに追加できる画像がありませんでした。");
        }

        document.Save(request.OutputPdfPath);

        return new ConversionResult(1, errors);
    }

    private static void ConfigurePage(PdfPage page, XImage image, ImagePageMode mode, bool useMargin)
    {
        if (mode == ImagePageMode.A4Portrait)
        {
            page.Size = PageSize.A4;
            page.Orientation = PageOrientation.Portrait;
            return;
        }

        if (mode == ImagePageMode.A4Landscape)
        {
            page.Size = PageSize.A4;
            page.Orientation = PageOrientation.Landscape;
            return;
        }

        var margin = useMargin ? MarginPoints : 0;
        page.Width = XUnit.FromPoint(Math.Max(1, image.PointWidth + margin * 2));
        page.Height = XUnit.FromPoint(Math.Max(1, image.PointHeight + margin * 2));
    }

    private static void DrawImage(XGraphics graphics, PdfPage page, XImage image, bool useMargin)
    {
        var margin = useMargin ? MarginPoints : 0;
        var pageWidth = page.Width.Point;
        var pageHeight = page.Height.Point;
        var maxWidth = Math.Max(1, pageWidth - margin * 2);
        var maxHeight = Math.Max(1, pageHeight - margin * 2);

        var scale = Math.Min(maxWidth / image.PointWidth, maxHeight / image.PointHeight);
        var drawWidth = image.PointWidth * scale;
        var drawHeight = image.PointHeight * scale;
        var x = (pageWidth - drawWidth) / 2;
        var y = (pageHeight - drawHeight) / 2;

        graphics.DrawImage(image, x, y, drawWidth, drawHeight);
    }

    private static void Validate(ImageToPdfRequest request)
    {
        if (request.ImageFiles.Count == 0)
        {
            throw new ArgumentException("JPEGまたはPNG画像を選択してください。");
        }

        if (string.IsNullOrWhiteSpace(request.OutputPdfPath))
        {
            throw new ArgumentException("出力PDF名と保存先を指定してください。");
        }

        foreach (var imageFile in request.ImageFiles)
        {
            if (!File.Exists(imageFile))
            {
                throw new FileNotFoundException("画像ファイルが見つかりません。", imageFile);
            }

            var extension = Path.GetExtension(imageFile);
            if (!string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("JPEGまたはPNG画像だけを選択してください。");
            }
        }
    }
}
