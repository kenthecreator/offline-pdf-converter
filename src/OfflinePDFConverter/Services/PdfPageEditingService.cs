using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace OfflinePDFConverter.Services;

internal readonly record struct PagePlacement(double X, double Y, double Width, double Height);

internal sealed class PdfLayout(
    int pageCount, int pagesPerSheet, bool landscape)
{
    private readonly Dictionary<int, PagePlacement> _placements = new();
    private const double Margin = 18;

    public static IReadOnlyList<int> PresetPageCounts { get; } = Array.AsReadOnly(new[] { 2, 4, 6, 9, 16 });

    public int PagesPerSheet { get; } = pagesPerSheet;
    public int SheetCount => (pageCount + PagesPerSheet - 1) / PagesPerSheet;
    public double Width => landscape ? 841.89 : 595.28;
    public double Height => landscape ? 595.28 : 841.89;

    public IEnumerable<int> PagesOnSheet(int sheet)
    {
        var first = sheet * PagesPerSheet;
        return Enumerable.Range(first, Math.Min(PagesPerSheet, pageCount - first));
    }

    public PagePlacement GetPlacement(int page, double aspect)
    {
        if (_placements.TryGetValue(page, out var edited))
            return edited;

        var columns = PagesPerSheet switch
        {
            2 => landscape ? 2 : 1,
            4 => 2,
            6 => landscape ? 3 : 2,
            9 => 3,
            16 => 4,
            _ => throw new InvalidOperationException("未対応のページ配置です。")
        };
        var rows = PagesPerSheet / columns;
        var slot = page % PagesPerSheet;
        var cellWidth = (Width - Margin * (columns + 1)) / columns;
        var cellHeight = (Height - Margin * (rows + 1)) / rows;
        var width = Math.Min(cellWidth, cellHeight * aspect);
        var height = width / aspect;
        return new PagePlacement(
            Margin + slot % columns * (cellWidth + Margin) + (cellWidth - width) / 2,
            Margin + slot / columns * (cellHeight + Margin) + (cellHeight - height) / 2,
            width, height);
    }

    public void SetPlacement(int page, PagePlacement placement) => _placements[page] = placement;

    public void ResetSheet(int sheet)
    {
        foreach (var page in PagesOnSheet(sheet))
            _placements.Remove(page);
    }
}

internal static class PdfPageEditingService
{
    private static PdfDocument Open(string path, string? password, PdfDocumentOpenMode mode) =>
        string.IsNullOrEmpty(password) ? PdfReader.Open(path, mode) : PdfReader.Open(path, password, mode);

    public static double[] GetPageAspects(string source, string? password)
    {
        using var document = Open(source, password, PdfDocumentOpenMode.Import);
        return Enumerable.Range(0, document.PageCount)
            .Select(index => GetGeometry(document.Pages[index]).Aspect)
            .ToArray();
    }

    public static int NormalizeRotation(int degrees) => (degrees % 360 + 360) % 360;

    public static string Rotate(string source, string? password, string destination,
        IReadOnlyList<int> rotations, CancellationToken token)
    {
        using var document = Open(source, password, PdfDocumentOpenMode.Modify);
        if (document.PageCount != rotations.Count)
            throw new InvalidOperationException("読み込み後に元のPDFのページ数が変わりました。");
        for (var i = 0; i < rotations.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            if (rotations[i] != 0)
                document.Pages[i].Rotate = NormalizeRotation(document.Pages[i].Rotate + rotations[i]);
        }
        return AtomicFile.Write(destination, path => document.Save(path), token);
    }

    public static string SaveLayout(string source, string? password, int pageCount, string destination,
        PdfLayout layout, CancellationToken token)
    {
        using var input = Open(source, password, PdfDocumentOpenMode.Import);
        if (input.PageCount != pageCount)
            throw new InvalidOperationException("読み込み後に元のPDFのページ数が変わりました。");
        using var buffer = new MemoryStream();
        // XPdfForm has no password parameter. Import to an in-memory PDF first for encrypted sources.
        if (!string.IsNullOrEmpty(password))
        {
            using var decrypted = new PdfDocument();
            for (var index = 0; index < input.PageCount; index++)
                decrypted.AddPage(input.Pages[index]);
            decrypted.Save(buffer, closeStream: false);
            buffer.Position = 0;
        }

        using var form = string.IsNullOrEmpty(password) ? XPdfForm.FromFile(source) : XPdfForm.FromStream(buffer);
        using var output = new PdfDocument();
        for (var sheet = 0; sheet < layout.SheetCount; sheet++)
        {
            token.ThrowIfCancellationRequested();
            var outputPage = output.AddPage();
            outputPage.Width = XUnit.FromPoint(layout.Width);
            outputPage.Height = XUnit.FromPoint(layout.Height);
            using var graphics = XGraphics.FromPdfPage(outputPage);
            foreach (var page in layout.PagesOnSheet(sheet))
            {
                form.PageNumber = page + 1;
                var sourcePage = form.Page ?? throw new InvalidDataException("元のPDFページを読み込めませんでした。");
                var geometry = GetGeometry(sourcePage);
                var placement = layout.GetPlacement(page, geometry.Aspect);
                var state = graphics.Save();
                var slot = new XRect(placement.X, placement.Y, placement.Width, placement.Height);
                graphics.IntersectClip(slot);
                graphics.DrawRectangle(XBrushes.White, slot);

                // PDFsharp rotates the form using the unrotated media size and subtracts the media origin
                // from its output matrix without applying the placement scale.
                var scaleX = placement.Width / geometry.DisplayCropWidth;
                var scaleY = placement.Height / geometry.DisplayCropHeight;
                var media = geometry.Media;
                var visualHeight = geometry.QuarterTurn ? media.Width : media.Height;
                graphics.DrawImage(form,
                    placement.X - geometry.CropOffset.X * scaleX + media.X1,
                    placement.Y - geometry.CropOffset.Y * scaleY + (visualHeight - media.Height) * scaleY - media.Y1,
                    media.Width * scaleX,
                    media.Height * scaleY);
                graphics.Restore(state);
            }
        }
        return AtomicFile.Write(destination, path => output.Save(path), token);
    }

    private static PageGeometry GetGeometry(PdfPage page)
    {
        var media = page.MediaBox;
        var crop = page.CropBox;
        var left = Math.Max(media.X1, crop.X1);
        var right = Math.Min(media.X2, crop.X2);
        var bottom = Math.Max(media.Y1, crop.Y1);
        var top = Math.Min(media.Y2, crop.Y2);
        if (right <= left || top <= bottom)
        {
            left = media.X1;
            bottom = media.Y1;
            right = media.X2;
            top = media.Y2;
        }

        var rotation = NormalizeRotation(page.Rotate);
        var quarterTurn = rotation is 90 or 270;
        var cropOffset = rotation switch
        {
            90 => new XPoint(bottom, left),
            180 => new XPoint(media.Width - right, bottom),
            270 => new XPoint(media.Height - top, media.Width - right),
            _ => new XPoint(left, media.Height - top)
        };
        return new PageGeometry(media, cropOffset, quarterTurn, quarterTurn ? top - bottom : right - left,
            quarterTurn ? right - left : top - bottom);
    }

    private readonly record struct PageGeometry(PdfRectangle Media, XPoint CropOffset, bool QuarterTurn,
        double DisplayCropWidth, double DisplayCropHeight)
    {
        public double Aspect => DisplayCropWidth / DisplayCropHeight;
    }
}
