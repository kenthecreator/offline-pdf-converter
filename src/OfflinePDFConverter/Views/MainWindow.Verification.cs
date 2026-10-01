using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using OfflinePDFConverter.Models;
using OfflinePDFConverter.Services;

namespace OfflinePDFConverter.Views;

public partial class MainWindow
{
    // Invoked only by the explicit --verify-gui command. Uses real controls and event handlers.
    internal async Task<int> VerifyGuiAsync(string reportPath)
    {
        var checks = new List<string>();
        Exception? failure = null;
        var root = Directory.CreateTempSubdirectory("OfflinePDFConverter-GUI-日本語-").FullName;
        var desktop = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        var report = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
        async Task Until(Func<bool> condition)
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.Elapsed > TimeSpan.FromSeconds(90)) throw new TimeoutException("GUI check did not finish.");
                await Task.Delay(50);
            }
        }
        async Task ClickAndWait(string name, string expectedDialog)
        {
            var button = Required<Button>(name);
            Check(button.IsEffectivelyEnabled && button.IsVisible, "Start button is unavailable: " + name);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var watch = Stopwatch.StartNew();
            var acknowledged = false;
            do
            {
                await Task.Delay(50);
                foreach (var dialog in desktop.Windows.Where(w => w != this).ToArray())
                {
                    Check(dialog.Title == expectedDialog, "Unexpected dialog: " + dialog.Title);
                    var text = string.Join("\n", dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
                    if (expectedDialog == "一括処理の結果") Check(text.Contains("失敗 0件") && text.Contains("中止・未処理 0件"), text);
                    dialog.Close(); acknowledged = true;
                }
                if (watch.Elapsed > TimeSpan.FromSeconds(90)) throw new TimeoutException("GUI operation timed out: " + name);
            } while (_conversionCts != null || !acknowledged);
        }
        try
        {
            Check(Title == AppIdentity.WindowTitle && IsVisible && Bounds.Width > 0, "Main window not shown.");
            for (var theme = 0; theme < 3; theme++)
            {
                FinishThemeDrag(theme);
                await Until(() => !_themeTransitionActive);
                Check(_themeMode == theme, "Theme switch failed.");
            }
            checks.Add("real desktop startup and Auto/Light/Dark theme controls");
            var image = Path.Combine(root, "日本語 image.png");
            using (var resource = typeof(MainWindow).Assembly.GetManifestResourceStream("OfflinePDFConverter.OcrSelfTest.png")!)
            using (var file = File.Create(image)) await resource.CopyToAsync(file);
            var imageFolder = Directory.CreateDirectory(Path.Combine(root, "image-output")).FullName;
            AddImagePaths(new[] { image }); SetMode(ConversionMode.ImageToPdf);
            _imageOutputPdfTextBox.Text = Path.Combine(imageFolder, "output.pdf"); _imageOutputBaseNameTextBox.Text = "output";
            await ClickAndWait("StartImageButton", "完了");
            var pdf = Directory.GetFiles(imageFolder, "*.pdf").Single();
            using (var doc = UglyToad.PdfPig.PdfDocument.Open(pdf)) Check(doc.NumberOfPages == 1, "GUI image PDF output invalid.");
            checks.Add("image to PDF through the actual Start control and completion dialog");
            await AddPdfPathsAsync(new[] { pdf }); SetMode(ConversionMode.PdfTools);
            await Until(() => _pdfPagePreviews.Count == 1);
            SetPdfPreviewDisplay(true); Check(_pdfPagePreviewListScroll.IsVisible, "List preview not visible.");
            SetPdfPreviewDisplay(false); Check(_pdfPagePreviewThumbnailScroll.IsVisible, "Icon preview not visible.");
            // Repeated changes exercise preview cancellation, replacement and bitmap lifetimes.
            for (var i = 0; i < 20; i++) { _pdfToolOperationCombo.SelectedIndex = i % 4; await Task.Delay(10); }
            _pdfToolOperationCombo.SelectedIndex = 1;
            await Until(() => _pdfPagePreviews.Count == 1);
            var split = Directory.CreateDirectory(Path.Combine(root, "split-output")).FullName;
            _pdfToolOutputFolderTextBox.Text = split;
            await ClickAndWait("StartPdfToolButton", "一括処理の結果");
            Check(Directory.GetFiles(split, "*.pdf").Length == 1, "GUI split output missing.");
            checks.Add("PDF preview icon/list switching, 20 operation changes and PDF split through Start");
            SetMode(ConversionMode.PdfToImage);
            var images = Directory.CreateDirectory(Path.Combine(root, "pdf-output")).FullName;
            _pdfOutputFolderTextBox.Text = images;
            await ClickAndWait("StartPdfButton", "一括処理の結果");
            Check(Directory.GetFiles(images, "*.png").Length == 1, "GUI raster output missing.");
            checks.Add("PDF to image through Start and successful batch results");
            SetMode(ConversionMode.PdfTools); _pdfToolOperationCombo.SelectedIndex = 4;
            await Until(() => _pdfPagePreviews.Count == 1);
            var editorTask = OpenSimpleEditWindowAsync(_pdfPagePreviews[0]);
            await Until(() => desktop.Windows.Any(w => w != this));
            var editor = desktop.Windows.Single(w => w != this);
            var editFont = editor.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "EditFontSizeTextBox");
            var addText = editor.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AddEditorTextButton");
            foreach (var invalid in new[] { "abc", "0", "NaN", "Infinity" })
            {
                editFont.Text = invalid;
                addText.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(editor.IsVisible, "Invalid editor input closed the editor.");
                Check(editor.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "EditorInputError").IsVisible,
                    "Invalid editor input did not show an explanation.");
            }
            editFont.Text = "14";
            addText.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            editor.GetVisualDescendants().OfType<TextBox>().Single(t => t.Classes.Contains("pdf-inline-editor")).Text = "日本語 ABC 12345";
            var stroke = editor.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "EditStrokeThicknessTextBox");
            var addShape = editor.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AddShapeRectangleButton");
            foreach (var invalid in new[] { "abc", "-1", "NaN", "Infinity" })
            {
                stroke.Text = invalid; addShape.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(editor.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "EditorInputError").IsVisible,
                    "Invalid shape thickness did not show an explanation.");
            }
            stroke.Text = "2"; addShape.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            editor.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "完了")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await editorTask;
            Check(_pdfTextEdits.Count == 1 && _pdfTextEdits[0].FontSize == 14 && _pdfTextEdits[0].Text == "日本語 ABC 12345"
                && _pdfShapeEdits.Count == 1 && _pdfShapeEdits[0].StrokeThickness == 2, "Editor did not recover after invalid inputs or lost inline text.");
            checks.Add("editor rejects invalid numeric input and permits subsequent valid text addition");
            var edited = Directory.CreateDirectory(Path.Combine(root, "edited-output")).FullName;
            _pdfToolOutputPdfTextBox.Text = Path.Combine(edited, "edited.pdf"); _pdfToolOutputPdfBaseNameTextBox.Text = "edited";
            await ClickAndWait("StartPdfToolButton", "完了");
            using (var editedPdf = UglyToad.PdfPig.PdfDocument.Open(Directory.GetFiles(edited, "*.pdf").Single()))
            {
                var extractedText = editedPdf.GetPage(1).Text;
                Check(extractedText.Contains("日本語 ABC 12345"), "Japanese GUI editing failed with the default embedded font. Extracted: " + extractedText);
            }
            checks.Add("Japanese GUI preview and PDF text/shape output using the default embedded font");

            SetMode(ConversionMode.Ocr);
            await Until(() => _ocrBeforePages.Count == 1);
            Required<RadioButton>("OcrMixedChoice").IsChecked = true;
            var ocr = Directory.CreateDirectory(Path.Combine(root, "ocr-output")).FullName;
            Required<TextBox>("OcrOutputFolderTextBox").Text = ocr;
            await ClickAndWait("StartOcrButton", "一括処理の結果");
            await Until(() => _ocrAfterPages.Count == 1);
            using (var doc = UglyToad.PdfPig.PdfDocument.Open(Directory.GetFiles(ocr, "*.pdf").Single()))
                Check(doc.GetPage(1).Text.Contains("日本語"), "GUI OCR text missing.");
            var viewerTask = OpenOcrPreviewWindowAsync(_ocrAfterPages[0], true);
            await Until(() => desktop.Windows.Any(w => w != this));
            var viewer = desktop.Windows.Single(w => w != this);
            var all = viewer.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "すべて選択");
            all.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var copy = viewer.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "選択した文字をコピー");
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(300);
            Check((await viewer.Clipboard!.TryGetTextAsync())?.Contains("日本語") == true, "PDF selection/copy did not reach the clipboard.");
            viewer.Close(); await viewerTask;
            checks.Add("OCR through Start, before/after previews and real PDF text selection/copy");
            using (var snapshot = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height), new Vector(96, 96)))
            { snapshot.Render(this); snapshot.Save(Path.ChangeExtension(report, ".png")); }
            // Closing an active conversion must cancel it and finish without displaying a modal result.
            SetMode(ConversionMode.PdfToImage); SetPdfDpiIndex(2);
            Required<Button>("StartPdfButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(_conversionCts != null, "Conversion not started for close test.");
            Check(!Required<Grid>("PdfPanel").IsEnabled && !Required<Grid>("PdfToolsPanel").IsEnabled, "Input panels remain editable during a conversion.");
            Close(); Check(_closeWhenIdle, "Close did not request cancellation.");
            await Until(() => _windowClosed);
            checks.Add("input controls disabled during conversion and close cancels safely");
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            CancelOcrPreview();
            _previewCts?.Cancel();
            foreach (var dialog in desktop.Windows.Where(w => w != this).ToArray()) dialog.Close();
            // Preview tasks use the input PDFs. Wait until cancellation has released them before cleanup.
            await Task.Delay(300);
            try { Directory.Delete(root, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failure ??= ex; }
        }
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { passed = failure == null, checks, error = failure?.ToString(),
            operatingSystem = Environment.OSVersion.ToString(), scope = "actual shipped executable with real Avalonia windows and control events" }, new JsonSerializerOptions { WriteIndented = true }));
        return failure == null ? 0 : 1;
    }
}
