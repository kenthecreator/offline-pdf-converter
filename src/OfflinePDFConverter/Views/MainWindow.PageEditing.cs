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
}
