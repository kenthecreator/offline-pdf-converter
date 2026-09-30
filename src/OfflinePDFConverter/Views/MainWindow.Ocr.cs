using Avalonia;
using Avalonia.Animation;
using Avalonia.Input;
using Avalonia.Threading;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using OfflinePDFConverter.Models;
using OfflinePDFConverter.Services;
using PDFtoImage;
using SkiaSharp;

namespace OfflinePDFConverter.Views;
#pragma warning disable CA1416 // PDF preview rendering uses PDFtoImage on supported desktop platforms.
public partial class MainWindow
{
    private Grid _ocrPanel = null!;
    private ListBox _ocrFilesList = null!;
    private RadioButton _ocrModeButton = null!;
    private Button _startOcrButton = null!;
    private CancellationTokenSource? _ocrPreviewCts;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _ocrOutputPaths = new();
    private List<PdfPagePreviewItem> _ocrBeforePages = new();
    private List<PdfPagePreviewItem> _ocrAfterPages = new();
    private int _ocrPreviewRevision;

    private void InitializeOcrScreen()
    {
        _ocrPanel = Required<Grid>("OcrPanel");
        _ocrFilesList = Required<ListBox>("OcrFilesList");
        _ocrModeButton = Required<RadioButton>("OcrModeButton");
        _startOcrButton = Required<Button>("StartOcrButton");
        _ocrFilesList.ItemsSource = _pdfFiles;
        Required<TextBox>("OcrOutputFolderTextBox").Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Required<TextBlock>("OcrEngineDescription").Text = AppIdentity.OcrDescription;
        Required<RadioButton>("OcrEnglishChoice").Content = AppIdentity.OcrLanguages[2];
        InitializeOcrPreviewMotion();
        UpdateOcrFiles();
        Closed += (_, _) => { CancelOcrPreview(); StopOcrScan(); DisposeOcrPages(_ocrBeforePages); DisposeOcrPages(_ocrAfterPages); };
    }

    private void UpdateOcrFiles()
    {
        if (_ocrPanel == null) return;
        Required<TextBlock>("OcrFilesEmptyHint").IsVisible = _pdfFiles.Count == 0;
        _startOcrButton.IsEnabled = _pdfFiles.Count > 0 && _conversionCts == null;
        if (_ocrFilesList.SelectedItem == null && _pdfFiles.Count > 0) _ocrFilesList.SelectedIndex = 0;
        if (_mode == ConversionMode.Ocr) RefreshOcrPreview();
    }

    private void OnOcrFilesSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_ocrPanel == null) return;
        RefreshOcrPreview();
    }

    private void CancelOcrPreview()
    {
        StopOcrResultAnimation();
        _ocrPreviewRevision++;
        _ocrPreviewCts?.Cancel();
        _ocrPreviewCts?.Dispose();
        _ocrPreviewCts = null;
    }

    private static void DisposeOcrPages(IEnumerable<PdfPagePreviewItem> pages)
    {
        foreach (var page in pages) page.Thumbnail.Dispose();
    }

    private void SetOcrPreviewPages(string side, List<PdfPagePreviewItem> pages)
    {
        var previous = side == "Before" ? _ocrBeforePages : _ocrAfterPages;
        Required<ItemsControl>($"Ocr{side}IconItems").ItemsSource = pages;
        Required<ItemsControl>($"Ocr{side}ListItems").ItemsSource = pages;
        if (side == "Before") _ocrBeforePages = pages; else _ocrAfterPages = pages;
        DisposeOcrPages(previous);
        if (side == "After" && pages.Count > 0) BeginOcrResultAnimation();
    }

    private void UpdateOcrPreviewDisplay()
    {
        var list = _isOcrPreviewListView;
        Required<RadioButton>("OcrPreviewIconButton").IsChecked = !list;
        Required<RadioButton>("OcrPreviewListButton").IsChecked = list;
        _ocrPreviewSelectionTransform.X = list ? PreviewViewDragMaximum : 0;
        foreach (var side in new[] { "Before", "After" })
        {
            Required<ScrollViewer>($"Ocr{side}IconScroll").IsVisible = !list;
            Required<ScrollViewer>($"Ocr{side}ListScroll").IsVisible = list;
        }
        Dispatcher.UIThread.Post(ApplyOcrScrollPosition, DispatcherPriority.Loaded);
    }

    private async void OnOcrPreviewOpenClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: PdfPagePreviewItem page }) return;
        var after = _ocrAfterPages.Contains(page);
        try { await OpenOcrPreviewWindowAsync(page, after); }
        catch (Exception ex) { await ShowMessageAsync("PDFを開けませんでした", FriendlyErrorFormatter.ToUserMessage(ex)); }
    }

    private static List<PdfPagePreviewItem> LoadOcrPreviews(string path, string? password, CancellationToken token)
    {
        var pages = new List<PdfPagePreviewItem>();
        try
        {
            using var stream = File.OpenRead(path);
            foreach (var bitmap in Conversion.ToImages(stream, password: password,
                options: new PDFtoImage.RenderOptions(Dpi: 100, WithAnnotations: true, BackgroundColor: SKColors.White, UseTiling: true)))
            {
                using (bitmap)
                {
                    token.ThrowIfCancellationRequested();
                    pages.Add(new PdfPagePreviewItem(path, pages.Count + 1, ToAvaloniaBitmap(bitmap),
                        bitmap.Width * .72, bitmap.Height * .72, Array.Empty<byte>(), false, ""));
                }
            }
            return pages;
        }
        catch { DisposeOcrPages(pages); throw; }
    }

    private async void RefreshOcrPreview()
    {
        if (_mode != ConversionMode.Ocr || _ocrPanel == null || _conversionCts != null) return;
        CancelOcrPreview();
        var revision = _ocrPreviewRevision;
        var file = _ocrFilesList.SelectedItem as FileItem ?? _pdfFiles.FirstOrDefault();
        SetOcrPreviewPages("Before", new()); SetOcrPreviewPages("After", new());
        Required<TextBlock>("OcrBeforeHint").Text = file == null ? "PDFを選択してください。" : "ページを読み込んでいます…";
        Required<TextBlock>("OcrAfterHint").Text = "OCR処理後に表示します。";
        if (file == null) return;
        _ocrPreviewCts = new CancellationTokenSource();
        var token = _ocrPreviewCts.Token;
        var password = _pdfPasswords.TryGetValue(file.Path, out var value) && !string.IsNullOrEmpty(value) ? value : null;
        foreach (var side in new[] { "Before", "After" })
        {
            var path = file.Path;
            if (side == "After" && (!_ocrOutputPaths.TryGetValue(file.Path, out path) || !File.Exists(path))) continue;
            try
            {
                var pages = await Task.Run(() => LoadOcrPreviews(path, side == "Before" ? password : null, token), token);
                if (revision != _ocrPreviewRevision || token.IsCancellationRequested) { DisposeOcrPages(pages); return; }
                SetOcrPreviewPages(side, pages);
                Required<TextBlock>($"Ocr{side}Hint").Text = $"{pages.Count}ページ · クリックして拡大";
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                if (revision != _ocrPreviewRevision) return;
                Required<TextBlock>($"Ocr{side}Hint").Text = "表示できません：" + FriendlyErrorFormatter.ToUserMessage(ex);
            }
        }
    }

    private void OnOcrAllPagesClick(object? sender, RoutedEventArgs e) => Required<TextBox>("OcrPagesTextBox").Text = string.Empty;

    private async void OnOcrOutputFolderClick(object? sender, RoutedEventArgs e)
    {
        var selected = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "PDFの保存先", AllowMultiple = false });
        if (selected.FirstOrDefault()?.TryGetLocalPath() is { } path) Required<TextBox>("OcrOutputFolderTextBox").Text = path;
    }

    private async void OnOcrLicensesClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var text = await Task.Run(AppIdentity.ReadOcrLicenses);
            var viewer = new Window { Title = "内蔵OCRの使用ライセンス", Width = 760, Height = 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new TextBox { Margin = new Avalonia.Thickness(16), Text = text, IsReadOnly = true,
                    AcceptsReturn = true, TextWrapping = TextWrapping.Wrap } };
            await viewer.ShowDialog(this);
        }
        catch (Exception ex) { await ShowMessageAsync("ライセンスを表示できませんでした", FriendlyErrorFormatter.ToUserMessage(ex)); }
    }

    private async void OnOfflineOcrClick(object? sender, RoutedEventArgs e)
    {
        if (_conversionCts != null) return;
        var files = _pdfFiles.Select(x => x.Path).ToArray();
        if (files.Length == 0) { await ShowMessageAsync("OCR PDFの作成", "PDFを追加してください。"); return; }
        var folder = Required<TextBox>("OcrOutputFolderTextBox").Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(folder)) { await ShowMessageAsync("保存先", "PDFの保存先フォルダを指定してください。"); return; }
        var selectedLanguage = Required<RadioButton>("OcrVerticalChoice").IsChecked == true ? "jpn_vert"
            : Required<RadioButton>("OcrEnglishChoice").IsChecked == true ? "eng"
            : Required<RadioButton>("OcrMixedChoice").IsChecked == true ? "jpn+eng" : "jpn";
        var pages = Required<TextBox>("OcrPagesTextBox").Text?.Trim() ?? string.Empty;
        var passwords = GetPdfPasswords();
        CancelOcrPreview();
        var service = new OfflineOcrService();
        await RunBatchAsync(files, async (file, progress, token) =>
        {
            await PrepareOcrScanAsync(file, pages, passwords.TryGetValue(file, out var sourcePassword) ? sourcePassword : null, token);
            var result = await service.CreateSearchablePdfBundledAsync(file,
                System.IO.Path.Combine(folder, System.IO.Path.GetFileNameWithoutExtension(file) + "_OCR.pdf"),
                selectedLanguage, pages, passwords.TryGetValue(file, out var password) ? password : "", progress, token);
            if (result.OutputPaths.FirstOrDefault() is { } output) _ocrOutputPaths[file] = output;
            return result;
        }, WaitForOcrScanAsync, StopOcrScan);
        RefreshOcrPreview();
    }

    private void OnCancelConversionClick(object? sender, RoutedEventArgs e)
    {
        _conversionCts?.Cancel();
        SetStatus("中止しています。現在の処理が終了するまでお待ちください。");
    }
}
