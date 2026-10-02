using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OfflinePDFConverter.Models;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PDFtoImage;
using SkiaSharp;

namespace OfflinePDFConverter.Services;

#pragma warning disable CA1416
public static class SearchablePdfService
{
    public static async Task<ConversionResult> CreateAsync(string source, string destination, string language,
        string pageSelection, string password, IProgress<ConversionProgress> progress, CancellationToken token,
        Func<string, CancellationToken, Task<IReadOnlyList<OcrTextBlock>>> recognize)
    {
        if (language is not ("jpn" or "jpn_vert" or "jpn+eng" or "jpn_vert+eng" or "eng")) throw new ArgumentException("認識言語を選択してください。");
        if (string.IsNullOrWhiteSpace(destination) || !Path.GetExtension(destination).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("PDFの保存先を指定してください。");
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("元のPDFとは別の保存先を指定してください。");
        token.ThrowIfCancellationRequested();
        using var original = PdfReader.Open(source, password, PdfDocumentOpenMode.Import);
        using var document = new PdfDocument();
        for (var index = 0; index < original.PageCount; index++) document.AddPage(original.Pages[index]);
        document.Info.Title = original.Info.Title;
        using var textDocument = UglyToad.PdfPig.PdfDocument.Open(source,
            new UglyToad.PdfPig.ParsingOptions { Password = password });
        var pages = string.IsNullOrWhiteSpace(pageSelection) ? Enumerable.Range(1, document.PageCount).ToArray()
            : PageRangeParser.Parse(pageSelection, document.PageCount, "認識する").ToArray();
        var directory = Directory.CreateTempSubdirectory("OfflinePDFConverter-Searchable-").FullName;
        try
        {
            GlobalFontSettings.FontResolver ??= new AppFontResolver();
            var completed = 0;
            foreach (var number in pages)
            {
                token.ThrowIfCancellationRequested();
                var textPage = textDocument.GetPage(number);
                // PdfPig retains letters outside CropBox; those hidden letters must not
                // make the visible image body look like an already searchable page.
                var existingLetters = textPage.Letters.Where(letter => !string.IsNullOrWhiteSpace(letter.Value)
                    && letter.BoundingBox.Right > 0 && letter.BoundingBox.Left < textPage.Width
                    && letter.BoundingBox.Top > 0 && letter.BoundingBox.Bottom < textPage.Height).ToArray();
                // A page number or short running header is not a body text layer.
                var marginOnly = existingLetters.Length <= 64 && existingLetters.All(letter =>
                    letter.BoundingBox.Top <= textPage.Height * 0.1 || letter.BoundingBox.Bottom >= textPage.Height * 0.9);
                if (existingLetters.Length > 0 && !marginOnly)
                {
                    progress.Report(new(++completed, pages.Length, $"{number}ページ目は既存の文字を保持しました"));
                    continue;
                }
                progress.Report(new(completed, pages.Length, $"{Path.GetFileName(source)}: {number}ページ目を文字認識しています"));
                var imagePath = Path.Combine(directory, "page.png");
                // Bound raster memory for large pages while keeping A4 at 300 dpi.
                var renderDpi = Math.Min(300, 4096 * 72 / Math.Max(textPage.Width, textPage.Height));
                int width, height;
                using (var stream = File.OpenRead(source))
                using (var bitmap = Conversion.ToImages(stream, new[] { number - 1 },
                    password: string.IsNullOrEmpty(password) ? null : password,
                    options: new RenderOptions(Dpi: (int)Math.Max(1, renderDpi), WithAnnotations: true, BackgroundColor: SKColors.White, UseTiling: true)).First())
                {
                    width = bitmap.Width; height = bitmap.Height;
                    using var image = File.Create(imagePath);
                    bitmap.Encode(image, SKEncodedImageFormat.Png, 100);
                }
                var blocks = await recognize(imagePath, token);
                token.ThrowIfCancellationRequested();
                // Omit duplicate recognized header/footer text; keep its original digital letters.
                if (existingLetters.Length > 0)
                {
                    blocks = blocks.Where(block =>
                    {
                        var left = block.X * textPage.Width / width;
                        var right = (block.X + block.Width) * textPage.Width / width;
                        var top = textPage.Height - block.Y * textPage.Height / height;
                        var bottom = textPage.Height - (block.Y + block.Height) * textPage.Height / height;
                        var overlapping = existingLetters.Where(letter =>
                        {
                            var box = letter.BoundingBox;
                            var area = Math.Max(0, Math.Min(right, box.Right) - Math.Max(left, box.Left))
                                * Math.Max(0, Math.Min(top, box.Top) - Math.Max(bottom, box.Bottom));
                            return area >= Math.Max(0.01, box.Width * box.Height) * 0.5;
                        }).ToArray();
                        return NormalizeText(block.Text) != NormalizeText(string.Concat(overlapping.Select(letter => letter.Value)));
                    }).ToArray();
                }
                AddTextLayer(document.Pages[number - 1], blocks, width, height, language.StartsWith("jpn_vert", StringComparison.Ordinal), token);
                progress.Report(new(++completed, pages.Length, $"{completed}/{pages.Length}ページの処理が完了しました"));
            }
            token.ThrowIfCancellationRequested();
            document.Info.Creator = AppIdentity.WindowTitle;
            var outputPath = AtomicFile.Write(destination, path => document.Save(path), token);
            return new ConversionResult(1, Array.Empty<string>()) { OutputPaths = new[] { outputPath } };
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string NormalizeText(string text) => string.Concat(text.Normalize(NormalizationForm.FormKC)
        .Where(character => !char.IsWhiteSpace(character)));

    public static IReadOnlyList<OcrTextBlock> ParseTsv(string tsv)
    {
        var blocks = new List<OcrTextBlock>();
        foreach (var line in tsv.Split('\n').Skip(1))
        {
            var cells = line.TrimEnd('\r').Split('\t', 12);
            if (cells.Length != 12 || cells[0] != "5" || string.IsNullOrWhiteSpace(cells[11])) continue;
            if (!double.TryParse(cells[6], CultureInfo.InvariantCulture, out var x)
                || !double.TryParse(cells[7], CultureInfo.InvariantCulture, out var y)
                || !double.TryParse(cells[8], CultureInfo.InvariantCulture, out var width)
                || !double.TryParse(cells[9], CultureInfo.InvariantCulture, out var height))
                throw new InvalidDataException("文字認識の位置情報を読み込めませんでした。");
            blocks.Add(new(cells[11], x, y, width, height));
        }
        return blocks;
    }

    public static void AddTextLayer(PdfPage page, IReadOnlyList<OcrTextBlock> blocks, int imageWidth,
        int imageHeight, bool vertical, CancellationToken token = default)
    {
        if (blocks.Count == 0) return; // Blank or unrecognized pages keep their original content.
        var crop = page.Elements.ContainsKey("/CropBox") ? page.CropBox : page.MediaBox;
        var rotation = ((page.Rotate % 360) + 360) % 360;
        var swapped = rotation is 90 or 270;
        var viewWidth = swapped ? crop.Height : crop.Width;
        var viewHeight = swapped ? crop.Width : crop.Height;
        var sx = viewWidth / imageWidth;
        var sy = viewHeight / imageHeight;
        // Convert top-left coordinates in the rendered CropBox to PDFsharp's page coordinates.
        XPoint Map(double x, double y)
        {
            var (px, py) = rotation switch
            {
                90 => (crop.X1 + y, crop.Y1 + x),
                180 => (crop.X2 - x, crop.Y1 + y),
                270 => (crop.X2 - y, crop.Y2 - x),
                _ => (crop.X1 + x, crop.Y2 - y)
            };
            return new XPoint(px, page.Height.Point - py);
        }
        using (var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
        {
            var font = new XFont("OfflinePDFConverterOcr", 10, XFontStyleEx.Regular,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
            foreach (var block in blocks)
            {
                token.ThrowIfCancellationRequested();
                if (block.Width <= 0 || block.Height <= 0) continue;
                var characters = block.Text.EnumerateRunes().ToArray();
                if (characters.Length == 0) continue;
                // Per-character positions allow selecting vertical Japanese without rotating glyphs.
                var uprightVertical = vertical && block.Height > block.Width * 1.5;
                var cellWidth = block.Width * sx / (uprightVertical ? 1 : characters.Length);
                var cellHeight = block.Height * sy / (uprightVertical ? characters.Length : 1);
                for (var i = 0; i < characters.Length; i++)
                {
                    var text = characters[i].ToString();
                    var x = block.X * sx + (uprightVertical ? 0 : i * cellWidth);
                    var y = block.Y * sy + (uprightVertical ? i * cellHeight : 0);
                    var origin = Map(x, y);
                    var right = Map(x + 1, y);
                    var down = Map(x, y + 1);
                    var state = graphics.Save();
                    graphics.MultiplyTransform(new XMatrix(right.X - origin.X, right.Y - origin.Y,
                        down.X - origin.X, down.Y - origin.Y, origin.X, origin.Y));
                    graphics.ScaleTransform(cellWidth / Math.Max(0.01, graphics.MeasureString(text, font).Width),
                        cellHeight / font.GetHeight());
                    graphics.DrawString(text, font, XBrushes.Black, new XPoint(0, 0), XStringFormats.TopLeft);
                    graphics.Restore(state);
                }
            }
        }
        // Only the new stream is modified. Rendering mode 3 makes text searchable but invisible;
        // original images, vector content, annotations and other page streams remain intact.
        var overlay = page.Contents.Elements.GetDictionary(page.Contents.Elements.Count - 1)!;
        var content = Encoding.Latin1.GetString(overlay.Stream.UnfilteredValue);
        content = content.Replace("BT\n", "BT\n3 Tr\n");
        content = Regex.Replace(content, @"(?m)\b[012] Tr\b", "3 Tr");
        overlay.Elements.Remove("/Filter");
        overlay.Stream.Value = Encoding.Latin1.GetBytes(content);
    }
}
