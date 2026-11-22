using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using OfflinePDFConverter.Models;
using OfflinePDFConverter.Services;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace OfflinePDFConverter.Views;

public partial class MainWindow
{
    private async Task VerifyV51Async(string root, string report, IList<string> checks, Func<Func<bool>, Task> until)
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        Window? Dialog() => desktop.Windows.FirstOrDefault(window => window != this);
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
        static Button FindButton(Window window, string caption) =>
            window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, caption));
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task DismissResultAsync(string title = "完了", params string[] expectedText)
        {
            await until(() => Dialog() is { } window && window is not PdfPageEditWindow);
            var result = Dialog()!;
            Require(result.Title == title, $"Unexpected result: {result.Title}");
            var message = string.Join("\n", result.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
            Require(expectedText.All(part => message.Contains(part, StringComparison.Ordinal)), message);
            result.Close();
            await until(() => _conversionCts == null);
        }
        async Task<PdfPageEditWindow> OpenEditorAsync(int operation, string name)
        {
            _pdfToolOperationCombo.SelectedIndex = operation;
            _pdfToolOutputPdfTextBox.Text = Path.Combine(root, name + ".pdf");
            _pdfToolOutputPdfBaseNameTextBox.Text = name;
            Click(_startPdfToolButton);
            await until(() => Dialog() != null);
            return Dialog() as PdfPageEditWindow ?? throw new InvalidDataException($"Unexpected window: {Dialog()?.Title}");
        }
        void Drag(Window window, Control target, Point local, Vector delta)
        {
            var start = target.TranslatePoint(local, window) ?? throw new InvalidDataException("Cannot locate drag target.");
            var pointer = new Avalonia.Input.Pointer(817, PointerType.Mouse, true);
            var held = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
            target.RaiseEvent(new PointerPressedEventArgs(target, pointer, window, start, 1, held, KeyModifiers.None));
            var captured = pointer.Captured ?? throw new InvalidDataException("The tile did not capture the pointer.");
            captured.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, captured, pointer, window,
                start + delta, 2, held, KeyModifiers.None));
            captured.RaiseEvent(new PointerReleasedEventArgs(captured, pointer, window, start + delta, 3,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        }

        const int totalPages = 17;
        var source = Path.Combine(root, "prototype.pdf");
        using (var document = new PdfDocument())
        {
            for (var page = 1; page <= totalPages; page++)
            {
                var sheet = document.AddPage();
                sheet.Width = XUnit.FromPoint(320);
                sheet.Height = XUnit.FromPoint(200);
                using var graphics = XGraphics.FromPdfPage(sheet);
                graphics.DrawRectangle(XBrushes.LightBlue, 10, 15, 300, 170);
                graphics.DrawString($"Slide-{page:D2}", new XFont("Arial", 22), XBrushes.Black, new XPoint(30, 65));
            }
            document.Save(source);
        }

        var previousOutput = _pdfToolOutputPdfTextBox.Text;
        var previousBaseName = _pdfToolOutputPdfBaseNameTextBox.Text;
        await AddPdfPathsAsync(new[] { source });
        SetMode(ConversionMode.PdfTools);

        foreach (var (count, columns, portrait) in new[]
                 {
                     (2, 2, false), (4, 2, false), (6, 3, false), (6, 2, true), (9, 3, false), (16, 4, false)
                 })
        {
            var name = $"layout-{count}-{(portrait ? "portrait" : "landscape")}";
            var layout = await OpenEditorAsync(5, name);
            var saveLayout = FindButton(layout, "PDFを保存");
            await until(() => saveLayout.IsEnabled);
            var selectors = layout.GetVisualDescendants().OfType<ComboBox>().ToArray();
            selectors.Single(box => box.Width == 120).SelectedIndex = PdfLayout.PresetPageCounts.ToList().IndexOf(count);
            await until(() => saveLayout.IsEnabled && layout.Layout.PagesPerSheet == count);
            if (portrait)
            {
                selectors.Single(box => box.Width == 100).SelectedIndex = 1;
                await until(() => saveLayout.IsEnabled && layout.Layout.Height > layout.Layout.Width);
            }

            var canvas = layout.GetVisualDescendants().OfType<Canvas>().Single(c => c.Children.Count == count);
            var first = layout.Layout.GetPlacement(0, 1.6);
            var lastInRow = layout.Layout.GetPlacement(columns - 1, 1.6);
            Require(lastInRow.Y == first.Y && lastInRow.X > first.X, "Incorrect first row placement.");
            if (count > columns)
            {
                var firstInNextRow = layout.Layout.GetPlacement(columns, 1.6);
                Require(firstInNextRow.X == first.X && firstInNextRow.Y > first.Y, "Incorrect second row placement.");
            }

            if (count == 16)
            {
                var tile = (Border)canvas.Children[0];
                await until(() => tile.Bounds.Width > 1);
                Drag(layout, tile, new Point(14, 14), new Vector(17, 10));
                var moved = layout.Layout.GetPlacement(0, 1.6);
                Require(moved.X > first.X && moved.Y > first.Y, "Drag did not move the tile.");
                var handle = (Border)((Grid)tile.Child!).Children[2];
                await until(() => handle.IsVisible && handle.Bounds.Width > 1);
                Drag(layout, handle, new Point(4, 4), new Vector(-25, -16));
                var resized = layout.Layout.GetPlacement(0, 1.6);
                Require(resized.Width < moved.Width && Math.Abs(resized.Width / resized.Height - 1.6) < .0001,
                    "The corner handle did not resize proportionally.");
                using var snapshot = new RenderTargetBitmap(
                    new PixelSize((int)layout.Bounds.Width, (int)layout.Bounds.Height), new Vector(96, 96));
                snapshot.Render(layout);
                snapshot.Save(Path.ChangeExtension(report, ".layout.png"));

                var next = FindButton(layout, "次へ");
                Click(next);
                await until(() => saveLayout.IsEnabled && canvas.Children.Count == 1);
                Click(FindButton(layout, "前へ"));
                await until(() => saveLayout.IsEnabled && canvas.Children.Count == 16);
                Require(layout.Layout.GetPlacement(0, 1.6) == resized, "Placement changed after switching sheets.");
            }

            Click(saveLayout);
            await DismissResultAsync();
            using var output = UglyToad.PdfPig.PdfDocument.Open(Path.Combine(root, $"prototype_{name}.pdf"));
            Require(output.NumberOfPages == (totalPages + count - 1) / count, "Incorrect sheet count.");
            for (var sheet = 1; sheet <= output.NumberOfPages; sheet++)
            {
                var page = output.GetPage(sheet);
                Require((page.Height > page.Width) == portrait, "Incorrect paper orientation.");
                var firstOnSheet = (sheet - 1) * count + 1;
                var position = -1;
                for (var i = firstOnSheet; i < firstOnSheet + count && i <= totalPages; i++)
                {
                    var found = page.Text.IndexOf($"Slide-{i:D2}", StringComparison.Ordinal);
                    Require(found > position, "Incorrect grouping, order, or missing text.");
                    position = found;
                }
            }
        }
        checks.Add("2/4/6/9/16 in 1, portrait and landscape grids, 16-up tile move/resize, sheet navigation, and PDF order/text");

        var rotation = await OpenEditorAsync(6, "rotation");
        var left = FindButton(rotation, "左に90°");
        var right = FindButton(rotation, "右に90°");
        await until(() => left.IsEnabled);
        Click(left);
        rotation.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = true;
        Click(right);
        var saveRotation = FindButton(rotation, "PDFを保存");
        Require(saveRotation.IsEnabled, "Rotation save is unavailable.");
        Click(saveRotation);
        await DismissResultAsync();
        using (var output = PdfReader.Open(Path.Combine(root, "prototype_rotation.pdf"), PdfDocumentOpenMode.Import))
            Require(output.Pages[0].Rotate == 0 && output.Pages[1].Rotate == 90 && output.PageCount == totalPages,
                "Saved rotations do not match single-page and all-page controls.");
        checks.Add("PDF Edit rotation of selected and all pages through the actual buttons, verified in the saved PDF");

        var fixtures = Environment.GetEnvironmentVariable("OPC_OFFICE_VERIFY_FIXTURES");
        if (!string.IsNullOrEmpty(fixtures))
        {
            SetMode(ConversionMode.PdfToImage);
            var officeButton = this.GetVisualDescendants().OfType<Button>()
                .Single(button => button.IsEffectivelyVisible && Equals(button.Content, "Officeファイルを追加…"));
            Require(officeButton.IsEffectivelyEnabled, "The Office file picker button is unavailable.");
            using var drop = new DataTransfer();
            async Task AddToDropAsync(string path)
            {
                var file = await StorageProvider.TryGetFileFromPathAsync(new Uri(path));
                drop.Add(DataTransferItem.CreateFile(file ?? throw new FileNotFoundException(path)));
            }

            foreach (var name in new[] { "日本語 サンプル.docx", "日本語 ワークブック.xlsx", "日本語 スライド.pptx" })
            {
                var path = Path.Combine(root, name);
                File.Copy(Path.Combine(fixtures, name), path);
                await AddToDropAsync(path);
            }
            const string escapedName = "日本語 # %20";
            var escapedPath = Path.Combine(root, escapedName + ".PPTX");
            File.Copy(Path.Combine(fixtures, "日本語 スライド.pptx"), escapedPath);
            var corruptPath = Path.Combine(root, "broken.docx");
            await File.WriteAllTextAsync(corruptPath, "This is not an Office file.");
            await AddToDropAsync(corruptPath);

            RaiseEvent(new DragEventArgs(DragDrop.DropEvent, drop, this, new Point(100, 160), KeyModifiers.None));
            await DismissResultAsync("変換結果", "作成したファイル: 3", "broken.docx");
            Require(!File.Exists(Path.ChangeExtension(corruptPath, ".pdf")), "An invalid input left a PDF behind.");
            // The native picker supplies its own storage item for literal %xx names. The
            // programmatic storage lookup on macOS sometimes canonicalizes that name.
            var escapedConversion = AddDroppedFilesAsync(new[] { escapedPath });
            await DismissResultAsync("完了", "作成したファイル: 1");
            await escapedConversion;
            using (var escaped = UglyToad.PdfPig.PdfDocument.Open(Path.ChangeExtension(escapedPath, ".pdf")))
                Require(escaped.GetPages().Any(page => page.Text.Contains("PowerPoint sample")),
                    "An Office file with a literal URL escape in its name did not convert.");
            using var word = UglyToad.PdfPig.PdfDocument.Open(Path.Combine(root, "日本語 サンプル.pdf"));
            using var excel = UglyToad.PdfPig.PdfDocument.Open(Path.Combine(root, "日本語 ワークブック.pdf"));
            using var slides = UglyToad.PdfPig.PdfDocument.Open(Path.Combine(root, "日本語 スライド.pdf"));
            Require(word.GetPages().Any(page => page.Text.Contains("Word sample"))
                && excel.GetPages().Any(page => page.Text.Contains("Excel sample"))
                && slides.GetPages().Any(page => page.Text.Contains("PowerPoint sample")),
                "Office file drop did not produce the expected PDFs.");
            var evidence = Path.ChangeExtension(report, ".office");
            Directory.CreateDirectory(evidence);
            foreach (var name in new[] { "日本語 サンプル.pdf", "日本語 ワークブック.pdf", "日本語 スライド.pdf" })
                File.Copy(Path.Combine(root, name), Path.Combine(evidence, name), overwrite: true);
            checks.Add("Word/Excel/PowerPoint file drop, percent in file names, corrupt-input reporting, searchable outputs, and the Office picker button");
        }

        _pdfFiles.Clear();
        _pdfToolOperationCombo.SelectedIndex = 0;
        _pdfToolOutputPdfTextBox.Text = previousOutput;
        _pdfToolOutputPdfBaseNameTextBox.Text = previousBaseName;
    }
}
