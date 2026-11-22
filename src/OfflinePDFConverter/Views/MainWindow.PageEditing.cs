using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OfflinePDFConverter.Models;
using OfflinePDFConverter.Services;

namespace OfflinePDFConverter.Views;

public partial class MainWindow
{
    private bool _openingPageEditor;

    private async Task StartPageEditingAsync(bool layoutMode)
    {
        if (_openingPageEditor || _conversionCts != null)
            return;
        if (_pdfFiles.Count != 1)
            throw new ArgumentException("編集するPDFを1つだけ追加してください。");

        var sources = new[] { _pdfFiles[0].Path };
        var source = sources[0];
        var passwords = GetPdfPasswords();
        passwords.TryGetValue(source, out var password);
        var destination = BuildOutputPdfPath(
            _pdfToolOutputPdfTextBox.Text?.Trim() ?? string.Empty,
            _pdfToolOutputPdfBaseNameTextBox.Text?.Trim() ?? string.Empty, sources);

        _openingPageEditor = true;
        try
        {
            var aspects = await Task.Run(() => PdfPageEditingService.GetPageAspects(source, password));
            if (_windowClosed || _conversionCts != null)
                return;
            if (aspects.Length == 0)
                throw new ArgumentException("PDFにページがありません。");

            var editor = new PdfPageEditWindow(source, password, aspects, layoutMode);
            if (!await editor.ShowDialog<bool>(this))
                return;

            await RunConversionAsync(async (progress, token) =>
            {
                progress.Report(new ConversionProgress(0, 1, "PDFを保存しています。"));
                var path = await Task.Run(() => layoutMode
                    ? PdfPageEditingService.SaveLayout(source, password, aspects.Length, destination, editor.Layout, token)
                    : PdfPageEditingService.Rotate(source, password, destination, editor.Rotations, token), token);
                _pdfToolOutputPdfTextBox.Text = path;
                return new ConversionResult(1, Array.Empty<string>()) { OutputPaths = new[] { path } };
            }, "編集後のPDFを保存しました。");
        }
        finally
        {
            _openingPageEditor = false;
        }
    }

    private async void OnOfficeToPdfClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "PDFに変換するWord・Excel・PowerPointファイルを追加",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Word・Excel・PowerPoint")
                    {
                        Patterns = OfficePdfService.Extensions.Select(extension => "*" + extension).ToArray(),
                        AppleUniformTypeIdentifiers = new[]
                        {
                            "org.openxmlformats.wordprocessingml.document",
                            "org.openxmlformats.spreadsheetml.sheet",
                            "org.openxmlformats.presentationml.presentation"
                        }
                    }
                }
            });
            await ConvertOfficeFilesAsync(files.Select(file => file.TryGetLocalPath()).WhereNotNull());
        }
        catch (Exception ex)
        {
            if (!_windowClosed) await ShowMessageAsync("変換できませんでした", FriendlyErrorFormatter.ToUserMessage(ex));
        }
    }

    private async Task ConvertOfficeFilesAsync(IEnumerable<string> paths)
    {
        if (_windowClosed || _conversionCts != null)
            return;
        var sources = paths.Where(OfficePdfService.IsSupported).Where(File.Exists).Distinct().ToArray();
        if (sources.Length == 0)
            return;

        var message = "元ファイルと同じフォルダーにPDFを保存しました。";
        await RunConversionAsync((progress, token) => OfficePdfService.ConvertAsync(sources, progress, token), message);
    }
}
