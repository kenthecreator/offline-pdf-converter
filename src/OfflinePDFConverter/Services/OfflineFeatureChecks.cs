using System.Security.Cryptography;
using OfflinePDFConverter.Models;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PDFtoImage;
using SkiaSharp;

namespace OfflinePDFConverter.Services;

#pragma warning disable CA1416
/// <summary>Exercises the actual shipped services, native libraries and embedded models.</summary>
internal static class OfflineFeatureChecks
{
    internal static async Task RunAsync(string root, List<string> checks)
    {
        GlobalFontSettings.FontResolver ??= new AppFontResolver();
        var progress = new InlineProgress(_ => { });
        var passwords = new Dictionary<string, string>();
        var service = new PdfDocumentService();
        var image = Path.Combine(root, "日本語 image.png");
        using (var resource = typeof(OfflineFeatureChecks).Assembly.GetManifestResourceStream("OfflinePDFConverter.OcrSelfTest.png")!)
        using (var file = File.Create(image)) await resource.CopyToAsync(file);
        var input = Path.Combine(root, "原本.pdf");
        using (var doc = new PdfDocument())
        {
            for (var i = 0; i < 3; i++)
            {
                var page = doc.AddPage(); page.Width = XUnit.FromPoint(300); page.Height = XUnit.FromPoint(200);
                using var graphics = XGraphics.FromPdfPage(page);
                using var bitmap = XImage.FromFile(image);
                graphics.DrawImage(bitmap, 10, 10, 280, 180);
            }
            doc.Save(input);
        }
        var original = SHA256.HashData(File.ReadAllBytes(input));
        var ocrService = new OfflineOcrService();
        foreach (var language in new[] { "jpn", "jpn_vert", "eng", "jpn+eng" })
        {
            var output = Path.Combine(root, "profile-" + language.Replace("+", "-") + ".pdf");
            await ocrService.CreateSearchablePdfBundledAsync(input, output, language, "1", "", progress, default);
            using var recognized = UglyToad.PdfPig.PdfDocument.Open(output);
            if (!recognized.GetPage(1).Text.Contains("12345")) throw new InvalidDataException("OCR profile failed: " + language);
            if (recognized.GetPage(2).Letters.Count != 0) throw new InvalidDataException("Unselected page changed.");
            byte[] Render(string path)
            {
                using var source = File.OpenRead(path);
                using var bitmap = Conversion.ToImages(source, new[] { 0 }, options: new RenderOptions(Dpi:100)).First();
                return bitmap.Bytes;
            }
            if (!Render(input).SequenceEqual(Render(output))) throw new InvalidDataException("OCR changed page appearance.");
        }
        checks.Add("all four OCR profiles, unselected-page preservation and pixel-identical output");
        string Folder(string name) => Directory.CreateDirectory(Path.Combine(root, name)).FullName;
        static void Check(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
        static void Pages(string folder, int count)
        {
            using var doc = PdfReader.Open(Directory.GetFiles(folder, "*.pdf").Single(), PdfDocumentOpenMode.Import);
            Check(doc.PageCount == count, "Unexpected output page count.");
        }
        async Task Reject(Func<Task> action, string folder)
        {
            try { await action(); throw new InvalidDataException("Invalid input was accepted."); }
            catch (Exception ex) when (ex is ArgumentException or IOException or PdfReaderException) { }
            Check(!Directory.EnumerateFiles(folder).Any(), "Invalid input left an output.");
        }
        // Exercise all exposed DPI settings and both native image encoders.
        foreach (var dpi in new[] { 150, 200, 300, 400, 600 })
        foreach (var format in new[] { PdfImageFormat.Png, PdfImageFormat.Jpeg })
        {
            var folder = Folder($"render-{dpi}-{format}");
            var result = await new PdfToImageService().ConvertAsync(new(new[] { input }, folder, "image", format, dpi, "2", passwords), progress, default);
            Check(!result.HasErrors && result.CreatedFiles == 1, "PDF image conversion failed.");
            using var bitmap = SKBitmap.Decode(Directory.GetFiles(folder).Single());
            Check(bitmap != null && Math.Abs(bitmap.Width - 300 * dpi / 72d) < 3, "Invalid raster dimensions.");
        }
        checks.Add("PDF to PNG/JPEG: all five DPI settings and selected-page output");
        foreach (var mode in Enum.GetValues<ImagePageMode>())
        {
            var folder = Folder("image-" + mode);
            var result = await new ImageToPdfService().ConvertAsync(new(new[] { image, image }, Path.Combine(folder, "images.pdf"), mode, true), progress, default);
            Check(!result.HasErrors, "Image PDF conversion failed."); Pages(folder, 2);
        }
        checks.Add("images to PDF: A4 portrait, landscape and original size");
        var merge = Folder("merge");
        await service.MergeAsync(new(new[] { input, input }, Path.Combine(merge, "merged.pdf"), passwords), progress, default); Pages(merge, 6);
        var split = Folder("split");
        await service.SplitAsync(new(new[] { input }, split, "split", passwords), progress, default);
        Check(Directory.GetFiles(split).Length == 3, "Split output count.");
        foreach (var file in Directory.GetFiles(split)) { using var doc = PdfReader.Open(file, PdfDocumentOpenMode.Import); Check(doc.PageCount == 1, "Split pages."); }
        var delete = Folder("delete");
        await service.DeletePagesAsync(new(new[] { input }, "2", Path.Combine(delete, "deleted.pdf"), passwords), progress, default); Pages(delete, 2);
        var extract = Folder("extract");
        await service.ExtractPagesAsync(new(new[] { input }, "3,1", Path.Combine(extract, "extracted.pdf"), passwords), progress, default); Pages(extract, 2);
        checks.Add("PDF merge, split, delete and noncontiguous extraction");
        var edit = Folder("edit");
        var text = new PdfTextEditItem(1, 20, 30, 260, 60, "日本語 ABC 12345", "OfflinePDFConverterBundled", 16, false, "none", "#000000", "Left", false, false);
        var shape = new PdfShapeEditItem(1, 30, 120, 50, 40, "Rectangle", "#FF0000", "#000000", 1, 0, 15);
        await service.SimpleEditAsync(new(new[] { input }, new[] { text }, new[] { shape }, Path.Combine(edit, "edited.pdf"), passwords), progress, default);
        using (var doc = UglyToad.PdfPig.PdfDocument.Open(Directory.GetFiles(edit).Single()))
            Check(doc.GetPage(1).Text.Contains("日本語 ABC 12345"), "Japanese edit font mapping.");
        checks.Add("Japanese text and rotated shape editing using an embedded font");
        var encrypted = Path.Combine(root, "暗号化.pdf");
        using (var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify))
        { doc.SecuritySettings.UserPassword = "test"; doc.SecuritySettings.OwnerPassword = "owner"; doc.Save(encrypted); }
        var protectedPasswords = new Dictionary<string, string> { [encrypted] = "test" };
        var protectedSplit = Folder("protected-split");
        await service.SplitAsync(new(new[] { encrypted }, protectedSplit, "split", protectedPasswords), progress, default);
        Check(Directory.GetFiles(protectedSplit).Length == 3, "Password input split failed.");
        var wrong = Folder("wrong-password");
        await Reject(() => service.SplitAsync(new(new[] { encrypted }, wrong, "split", passwords), progress, default), wrong);
        checks.Add("password-protected input and rejection of an incorrect password");
        var invalid = Folder("invalid-pages");
        foreach (var range in new[] { "0", "1--3", ",、，", "1-2147483647" })
            await Reject(() => service.ExtractPagesAsync(new(new[] { input }, range, Path.Combine(invalid, "invalid.pdf"), passwords), progress, default), invalid);
        var malformed = Path.Combine(root, "broken.pdf"); File.WriteAllText(malformed, "broken");
        var broken = Folder("broken");
        var brokenResult = await new PdfToImageService().ConvertAsync(new(new[] { malformed }, broken, "broken", PdfImageFormat.Png, 150, "", passwords), progress, default);
        Check(brokenResult.HasErrors && !Directory.EnumerateFiles(broken).Any(), "Broken PDF was not rejected.");
        checks.Add("malformed PDFs and invalid page ranges fail without partial outputs");
        var collision = Folder("collision"); var desired = Path.Combine(collision, "out.pdf"); Directory.CreateDirectory(desired);
        var first = AtomicFile.Write(desired, path => File.WriteAllText(path, "first"));
        var second = AtomicFile.Write(desired, path => File.WriteAllText(path, "second"));
        Check(first != second && File.ReadAllText(first) == "first" && Directory.Exists(desired), "Output collision overwrote a file or directory.");
        checks.Add("same-name file and directory protection");
        var cancelled = Folder("cancelled");
        using (var cts = new CancellationTokenSource())
        {
            var result = await BatchRunner.RunAsync(new[] { input, encrypted }, (file, _, token) => new PdfToImageService().ConvertAsync(
                new(new[] { file }, cancelled, "cancelled", PdfImageFormat.Png, 150, "", passwords),
                new InlineProgress(value => { if (value.Completed > 0) cts.Cancel(); }), token), progress, cts.Token);
            Check(result.CreatedFiles == 1 && result.Items[0].Status == FileConversionStatus.Cancelled && result.Items[1].Status == FileConversionStatus.NotStarted
                && Directory.GetFiles(cancelled).Length == 1, "Cancellation lost saved files or result counts.");
        }
        checks.Add("in-flight cancellation retains exactly the saved pages and reports them");
        Check(original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(input))), "Source PDF changed.");
        checks.Add("source PDF remains byte-identical after all operations");
    }
    private sealed class InlineProgress(Action<ConversionProgress> action) : IProgress<ConversionProgress>
    { public void Report(ConversionProgress value) => action(value); }
}
