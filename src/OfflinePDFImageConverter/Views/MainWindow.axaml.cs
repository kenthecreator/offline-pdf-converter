using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using OfflinePDFImageConverter.Models;
using OfflinePDFImageConverter.Services;
using PDFtoImage;
using SkiaSharp;

namespace OfflinePDFImageConverter.Views;

#pragma warning disable CA1416 // PDF preview rendering uses PDFtoImage on supported desktop platforms.

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType PdfFileType = new("PDFファイル")
    {
        Patterns = new[] { "*.pdf" },
        MimeTypes = new[] { "application/pdf" }
    };

    private static readonly FilePickerFileType ImageFileType = new("JPEG / PNG画像")
    {
        Patterns = new[] { "*.jpg", "*.jpeg", "*.png" },
        MimeTypes = new[] { "image/jpeg", "image/png" }
    };

    private readonly ObservableCollection<FileItem> _pdfFiles = new();
    private readonly ObservableCollection<FileItem> _imageFiles = new();
    private readonly ObservableCollection<PdfPagePreviewItem> _pdfPagePreviews = new();
    private readonly IPdfToImageService _pdfToImageService = new PdfToImageService();
    private readonly IImageToPdfService _imageToPdfService = new ImageToPdfService();
    private readonly IPdfDocumentService _pdfDocumentService = new PdfDocumentService();
    private Button _pdfModeButton = null!;
    private Button _imageModeButton = null!;
    private Button _pdfToolsModeButton = null!;
    private Grid _pdfPanel = null!;
    private Grid _imagePanel = null!;
    private Grid _pdfToolsPanel = null!;
    private ListBox _pdfFilesList = null!;
    private ListBox _pdfToolFilesList = null!;
    private ScrollViewer _pdfPagePreviewThumbnailScroll = null!;
    private ScrollViewer _pdfPagePreviewListScroll = null!;
    private ItemsControl _pdfPagePreviewThumbnailItems = null!;
    private ItemsControl _pdfPagePreviewListItems = null!;
    private ListBox _imageFilesList = null!;
    private ComboBox _pdfFormatCombo = null!;
    private ComboBox _pdfDpiCombo = null!;
    private ComboBox _imagePageModeCombo = null!;
    private CheckBox _imageMarginCheckBox = null!;
    private ComboBox _pdfToolOperationCombo = null!;
    private Button _pdfPreviewIconButton = null!;
    private Button _pdfPreviewListButton = null!;
    private StackPanel _pdfToolOutputPdfPanel = null!;
    private StackPanel _pdfToolOutputFolderPanel = null!;
    private StackPanel _pdfDeletePagesPanel = null!;
    private TextBlock _pdfToolOutputPdfLabel = null!;
    private TextBox _pdfOutputFolderTextBox = null!;
    private TextBox _imageOutputPdfTextBox = null!;
    private TextBox _pdfToolOutputPdfTextBox = null!;
    private TextBox _pdfToolOutputFolderTextBox = null!;
    private TextBox _pdfDeletePagesTextBox = null!;
    private TextBlock _pdfPreviewHelpText = null!;
    private Button _startPdfButton = null!;
    private Button _startImageButton = null!;
    private Button _startPdfToolButton = null!;
    private Button _cancelButton = null!;
    private ProgressBar _mainProgressBar = null!;
    private TextBlock _statusText = null!;
    private ConversionMode _mode = ConversionMode.PdfToImage;
    private CancellationTokenSource? _conversionCts;
    private CancellationTokenSource? _previewCts;
    private bool _isPdfPreviewListView;

    public MainWindow()
    {
        InitializeComponent();
        BindControls();

        _pdfFilesList.ItemsSource = _pdfFiles;
        _pdfToolFilesList.ItemsSource = _pdfFiles;
        _pdfPagePreviewThumbnailItems.ItemsSource = _pdfPagePreviews;
        _pdfPagePreviewListItems.ItemsSource = _pdfPagePreviews;
        _imageFilesList.ItemsSource = _imageFiles;

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        _pdfOutputFolderTextBox.Text = desktop;
        _imageOutputPdfTextBox.Text = Path.Combine(desktop, "converted_images.pdf");
        _pdfToolOutputFolderTextBox.Text = desktop;
        _pdfToolOutputPdfTextBox.Text = Path.Combine(desktop, "merged.pdf");
        UpdatePdfToolOperationUi();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void BindControls()
    {
        _pdfModeButton = Required<Button>("PdfModeButton");
        _imageModeButton = Required<Button>("ImageModeButton");
        _pdfToolsModeButton = Required<Button>("PdfToolsModeButton");
        _pdfPanel = Required<Grid>("PdfPanel");
        _imagePanel = Required<Grid>("ImagePanel");
        _pdfToolsPanel = Required<Grid>("PdfToolsPanel");
        _pdfFilesList = Required<ListBox>("PdfFilesList");
        _pdfToolFilesList = Required<ListBox>("PdfToolFilesList");
        _pdfPagePreviewThumbnailScroll = Required<ScrollViewer>("PdfPagePreviewThumbnailScroll");
        _pdfPagePreviewListScroll = Required<ScrollViewer>("PdfPagePreviewListScroll");
        _pdfPagePreviewThumbnailItems = Required<ItemsControl>("PdfPagePreviewThumbnailItems");
        _pdfPagePreviewListItems = Required<ItemsControl>("PdfPagePreviewListItems");
        _imageFilesList = Required<ListBox>("ImageFilesList");
        _pdfFormatCombo = Required<ComboBox>("PdfFormatCombo");
        _pdfDpiCombo = Required<ComboBox>("PdfDpiCombo");
        _imagePageModeCombo = Required<ComboBox>("ImagePageModeCombo");
        _imageMarginCheckBox = Required<CheckBox>("ImageMarginCheckBox");
        _pdfToolOperationCombo = Required<ComboBox>("PdfToolOperationCombo");
        _pdfPreviewIconButton = Required<Button>("PdfPreviewIconButton");
        _pdfPreviewListButton = Required<Button>("PdfPreviewListButton");
        _pdfToolOutputPdfPanel = Required<StackPanel>("PdfToolOutputPdfPanel");
        _pdfToolOutputFolderPanel = Required<StackPanel>("PdfToolOutputFolderPanel");
        _pdfDeletePagesPanel = Required<StackPanel>("PdfDeletePagesPanel");
        _pdfToolOutputPdfLabel = Required<TextBlock>("PdfToolOutputPdfLabel");
        _pdfOutputFolderTextBox = Required<TextBox>("PdfOutputFolderTextBox");
        _imageOutputPdfTextBox = Required<TextBox>("ImageOutputPdfTextBox");
        _pdfToolOutputPdfTextBox = Required<TextBox>("PdfToolOutputPdfTextBox");
        _pdfToolOutputFolderTextBox = Required<TextBox>("PdfToolOutputFolderTextBox");
        _pdfDeletePagesTextBox = Required<TextBox>("PdfDeletePagesTextBox");
        _pdfPreviewHelpText = Required<TextBlock>("PdfPreviewHelpText");
        _startPdfButton = Required<Button>("StartPdfButton");
        _startImageButton = Required<Button>("StartImageButton");
        _startPdfToolButton = Required<Button>("StartPdfToolButton");
        _cancelButton = Required<Button>("CancelButton");
        _mainProgressBar = Required<ProgressBar>("MainProgressBar");
        _statusText = Required<TextBlock>("StatusText");
    }

    private T Required<T>(string name)
        where T : Control
    {
        return this.FindControl<T>(name)
            ?? throw new InvalidOperationException($"UI部品 '{name}' が見つかりません。");
    }

    private void OnPdfModeClick(object? sender, RoutedEventArgs e)
    {
        SetMode(ConversionMode.PdfToImage);
    }

    private void OnImageModeClick(object? sender, RoutedEventArgs e)
    {
        SetMode(ConversionMode.ImageToPdf);
    }

    private void OnPdfToolsModeClick(object? sender, RoutedEventArgs e)
    {
        SetMode(ConversionMode.PdfTools);
    }

    private void SetMode(ConversionMode mode)
    {
        _mode = mode;
        _pdfPanel.IsVisible = mode == ConversionMode.PdfToImage;
        _imagePanel.IsVisible = mode == ConversionMode.ImageToPdf;
        _pdfToolsPanel.IsVisible = mode == ConversionMode.PdfTools;
        SetClass(_pdfModeButton, "active", mode == ConversionMode.PdfToImage);
        SetClass(_imageModeButton, "active", mode == ConversionMode.ImageToPdf);
        SetClass(_pdfToolsModeButton, "active", mode == ConversionMode.PdfTools);
        _statusText.Text = mode switch
        {
            ConversionMode.PdfToImage => "PDFファイルを選ぶか、この画面にドロップしてください。",
            ConversionMode.ImageToPdf => "画像ファイルを選ぶか、この画面にドロップしてください。",
            _ => "PDF操作を選び、PDFファイルを追加してください。"
        };

        if (mode == ConversionMode.PdfTools)
        {
            RefreshPdfToolPreview();
        }
    }

    private static void SetClass(Control control, string className, bool enabled)
    {
        if (enabled)
        {
            if (!control.Classes.Contains(className))
            {
                control.Classes.Add(className);
            }
        }
        else
        {
            control.Classes.Remove(className);
        }
    }

    private async void OnAddPdfFilesClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "PDFファイルを選択",
            AllowMultiple = true,
            FileTypeFilter = new[] { PdfFileType }
        });

        AddPdfPaths(files.Select(file => file.TryGetLocalPath()).WhereNotNull());
    }

    private async void OnAddImageFilesClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "画像ファイルを選択",
            AllowMultiple = true,
            FileTypeFilter = new[] { ImageFileType }
        });

        AddImagePaths(files.Select(file => file.TryGetLocalPath()).WhereNotNull());
    }

    private async void OnSelectPdfOutputFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "画像の出力先フォルダを選択",
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(folder))
        {
            _pdfOutputFolderTextBox.Text = folder;
        }
    }

    private async void OnSelectImageOutputPdfClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "出力PDFを保存",
            SuggestedFileName = "converted_images.pdf",
            DefaultExtension = "pdf",
            FileTypeChoices = new[] { PdfFileType }
        });

        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            _imageOutputPdfTextBox.Text = path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                ? path
                : $"{path}.pdf";
        }
    }

    private async void OnSelectPdfToolOutputPdfClick(object? sender, RoutedEventArgs e)
    {
        var operation = GetPdfToolOperation();
        var suggestedName = operation == PdfToolOperation.DeletePages ? "deleted_pages.pdf" : "merged.pdf";
        var title = operation == PdfToolOperation.DeletePages ? "ページ削除後のPDFを保存" : "結合後のPDFを保存";

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = "pdf",
            FileTypeChoices = new[] { PdfFileType }
        });

        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            _pdfToolOutputPdfTextBox.Text = EnsurePdfExtension(path);
        }
    }

    private async void OnSelectPdfToolOutputFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "分割したPDFの出力先フォルダを選択",
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(folder))
        {
            _pdfToolOutputFolderTextBox.Text = folder;
        }
    }

    private void OnRemovePdfFilesClick(object? sender, RoutedEventArgs e)
    {
        RemoveSelected(_mode == ConversionMode.PdfTools ? _pdfToolFilesList : _pdfFilesList, _pdfFiles);
        RefreshPdfToolPreview();
    }

    private void OnRemoveImageFilesClick(object? sender, RoutedEventArgs e)
    {
        RemoveSelected(_imageFilesList, _imageFiles);
    }

    private void OnClearPdfFilesClick(object? sender, RoutedEventArgs e)
    {
        _pdfFiles.Clear();
        RefreshPdfToolPreview();
    }

    private void OnClearImageFilesClick(object? sender, RoutedEventArgs e)
    {
        _imageFiles.Clear();
    }

    private void OnMoveImageUpClick(object? sender, RoutedEventArgs e)
    {
        MoveSelectedImage(-1);
    }

    private void OnMoveImageDownClick(object? sender, RoutedEventArgs e)
    {
        MoveSelectedImage(1);
    }

    private void OnMovePdfUpClick(object? sender, RoutedEventArgs e)
    {
        MoveSelectedPdf(-1);
    }

    private void OnMovePdfDownClick(object? sender, RoutedEventArgs e)
    {
        MoveSelectedPdf(1);
    }

    private void OnPdfToolOperationChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_pdfToolOperationCombo == null)
        {
            return;
        }

        UpdatePdfToolOperationUi();
        RefreshPdfToolPreview();
    }

    private void OnRefreshPdfPreviewClick(object? sender, RoutedEventArgs e)
    {
        RefreshPdfToolPreview();
    }

    private void OnPdfPreviewIconClick(object? sender, RoutedEventArgs e)
    {
        _isPdfPreviewListView = false;
        UpdatePdfPreviewDisplay();
    }

    private void OnPdfPreviewListClick(object? sender, RoutedEventArgs e)
    {
        _isPdfPreviewListView = true;
        UpdatePdfPreviewDisplay();
    }

    private void OnPdfPreviewDeleteSelectionChanged(object? sender, RoutedEventArgs e)
    {
        if (GetPdfToolOperation() != PdfToolOperation.DeletePages)
        {
            return;
        }

        if (sender is CheckBox { DataContext: PdfPagePreviewItem item } checkBox)
        {
            item.IsMarkedForDelete = checkBox.IsChecked == true;
        }

        var pages = _pdfPagePreviews
            .Where(item => item.IsMarkedForDelete)
            .Select(item => item.PageNumber)
            .Distinct()
            .Order()
            .ToList();

        _pdfDeletePagesTextBox.Text = FormatPageRanges(pages);
    }

    private async void OnStartPdfConversionClick(object? sender, RoutedEventArgs e)
    {
        var request = new PdfToImageRequest(
            _pdfFiles.Select(item => item.Path).ToList(),
            _pdfOutputFolderTextBox.Text?.Trim() ?? string.Empty,
            GetPdfImageFormat(),
            GetPdfDpi());

        await RunConversionAsync(
            (progress, token) => _pdfToImageService.ConvertAsync(request, progress, token),
            "PDFから画像への変換が完了しました。");
    }

    private async void OnStartImageConversionClick(object? sender, RoutedEventArgs e)
    {
        var outputPdfPath = _imageOutputPdfTextBox.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(outputPdfPath)
            && !outputPdfPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            outputPdfPath = $"{outputPdfPath}.pdf";
            _imageOutputPdfTextBox.Text = outputPdfPath;
        }

        var request = new ImageToPdfRequest(
            _imageFiles.Select(item => item.Path).ToList(),
            outputPdfPath,
            GetImagePageMode(),
            _imageMarginCheckBox.IsChecked == true);

        await RunConversionAsync(
            (progress, token) => _imageToPdfService.ConvertAsync(request, progress, token),
            "画像からPDFへの変換が完了しました。");
    }

    private async void OnStartPdfToolClick(object? sender, RoutedEventArgs e)
    {
        switch (GetPdfToolOperation())
        {
            case PdfToolOperation.Merge:
                await StartMergePdfAsync();
                break;
            case PdfToolOperation.Split:
                await StartSplitPdfAsync();
                break;
            case PdfToolOperation.DeletePages:
                await StartDeletePdfPagesAsync();
                break;
        }
    }

    private async Task StartMergePdfAsync()
    {
        var outputPdfPath = EnsurePdfExtension(_pdfToolOutputPdfTextBox.Text?.Trim() ?? string.Empty);
        _pdfToolOutputPdfTextBox.Text = outputPdfPath;

        var request = new PdfMergeRequest(
            _pdfFiles.Select(item => item.Path).ToList(),
            outputPdfPath);

        await RunConversionAsync(
            (progress, token) => _pdfDocumentService.MergeAsync(request, progress, token),
            "PDFの結合が完了しました。");
    }

    private async Task StartSplitPdfAsync()
    {
        var request = new PdfSplitRequest(
            _pdfFiles.Select(item => item.Path).ToList(),
            _pdfToolOutputFolderTextBox.Text?.Trim() ?? string.Empty);

        await RunConversionAsync(
            (progress, token) => _pdfDocumentService.SplitAsync(request, progress, token),
            "PDFの分割が完了しました。");
    }

    private async Task StartDeletePdfPagesAsync()
    {
        var outputPdfPath = EnsurePdfExtension(_pdfToolOutputPdfTextBox.Text?.Trim() ?? string.Empty);
        _pdfToolOutputPdfTextBox.Text = outputPdfPath;

        var request = new PdfDeletePagesRequest(
            _pdfFiles.Select(item => item.Path).ToList(),
            _pdfDeletePagesTextBox.Text?.Trim() ?? string.Empty,
            outputPdfPath);

        await RunConversionAsync(
            (progress, token) => _pdfDocumentService.DeletePagesAsync(request, progress, token),
            "指定ページを削除したPDFを作成しました。");
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _conversionCts?.Cancel();
        _statusText.Text = "中止しています...";
    }

    private async Task RunConversionAsync(
        Func<IProgress<ConversionProgress>, CancellationToken, Task<ConversionResult>> action,
        string successMessage)
    {
        if (_conversionCts != null)
        {
            return;
        }

        _conversionCts = new CancellationTokenSource();
        var progress = new Progress<ConversionProgress>(UpdateProgress);
        SetBusy(true);
        _mainProgressBar.Value = 0;

        try
        {
            var result = await action(progress, _conversionCts.Token);
            _mainProgressBar.Value = 100;

            if (result.HasErrors)
            {
                var heading = result.CreatedFiles == 0
                    ? "変換できたファイルはありませんでした。"
                    : "一部のファイルは変換できませんでした。";
                var message = $"{heading}\n\n作成したファイル: {result.CreatedFiles}\n\n{string.Join("\n", result.Errors.Take(5))}";
                _statusText.Text = heading;
                await ShowMessageAsync("変換結果", message);
            }
            else
            {
                var message = $"{successMessage}\n\n作成したファイル: {result.CreatedFiles}";
                _statusText.Text = successMessage;
                await ShowMessageAsync("完了", message);
            }
        }
        catch (OperationCanceledException)
        {
            _mainProgressBar.Value = 0;
            _statusText.Text = "処理を中止しました。";
        }
        catch (Exception ex)
        {
            _mainProgressBar.Value = 0;
            var message = FriendlyErrorFormatter.ToUserMessage(ex);
            _statusText.Text = message;
            await ShowMessageAsync("変換できませんでした", message);
        }
        finally
        {
            _conversionCts.Dispose();
            _conversionCts = null;
            SetBusy(false);
        }
    }

    private void UpdateProgress(ConversionProgress progress)
    {
        _mainProgressBar.Value = progress.Percent;
        _statusText.Text = progress.Message;
    }

    private void SetBusy(bool busy)
    {
        _startPdfButton.IsEnabled = !busy;
        _startImageButton.IsEnabled = !busy;
        _startPdfToolButton.IsEnabled = !busy;
        _cancelButton.IsEnabled = busy;
        _pdfModeButton.IsEnabled = !busy;
        _imageModeButton.IsEnabled = !busy;
        _pdfToolsModeButton.IsEnabled = !busy;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Formats.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var dropped = e.DataTransfer.TryGetFiles();
        if (dropped == null)
        {
            return;
        }

        var paths = ExpandDroppedPaths(dropped.Select(file => file.TryGetLocalPath()).WhereNotNull());

        if (_mode == ConversionMode.PdfToImage)
        {
            AddPdfPaths(paths);
        }
        else if (_mode == ConversionMode.PdfTools)
        {
            AddPdfPaths(paths);
        }
        else
        {
            AddImagePaths(paths);
        }
    }

    private void AddPdfPaths(IEnumerable<string> paths)
    {
        AddPaths(paths.Where(IsPdfFile), _pdfFiles);
        _statusText.Text = _mode == ConversionMode.PdfTools
            ? $"{_pdfFiles.Count}件のPDFが選択されています。結合時は一覧の順番どおりに並びます。"
            : $"{_pdfFiles.Count}件のPDFが選択されています。";

        RefreshPdfToolPreview();
    }

    private void AddImagePaths(IEnumerable<string> paths)
    {
        AddPaths(paths.Where(IsImageFile), _imageFiles);
        _statusText.Text = $"{_imageFiles.Count}件の画像が選択されています。";
    }

    private static void AddPaths(IEnumerable<string> paths, ObservableCollection<FileItem> target)
    {
        var existing = target.Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(path) && existing.Add(path))
            {
                target.Add(new FileItem(path));
            }
        }
    }

    private static IEnumerable<string> ExpandDroppedPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                yield return path;
            }
            else if (Directory.Exists(path))
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(path);
                }
                catch
                {
                    continue;
                }

                foreach (var file in files)
                {
                    yield return file;
                }
            }
        }
    }

    private static void RemoveSelected(ListBox listBox, ObservableCollection<FileItem> target)
    {
        var selected = listBox.SelectedItems?.Cast<FileItem>().ToList() ?? new List<FileItem>();
        foreach (var item in selected)
        {
            target.Remove(item);
        }
    }

    private void MoveSelectedImage(int offset)
    {
        if (_imageFilesList.SelectedItem is not FileItem selected)
        {
            return;
        }

        var index = _imageFiles.IndexOf(selected);
        var newIndex = index + offset;
        if (index < 0 || newIndex < 0 || newIndex >= _imageFiles.Count)
        {
            return;
        }

        _imageFiles.Move(index, newIndex);
        _imageFilesList.SelectedItem = selected;
    }

    private void MoveSelectedPdf(int offset)
    {
        if (_pdfToolFilesList.SelectedItem is not FileItem selected)
        {
            return;
        }

        var index = _pdfFiles.IndexOf(selected);
        var newIndex = index + offset;
        if (index < 0 || newIndex < 0 || newIndex >= _pdfFiles.Count)
        {
            return;
        }

        _pdfFiles.Move(index, newIndex);
        _pdfToolFilesList.SelectedItem = selected;
        RefreshPdfToolPreview();
    }

    private PdfImageFormat GetPdfImageFormat()
    {
        return GetComboText(_pdfFormatCombo).Contains("JPEG", StringComparison.OrdinalIgnoreCase)
            ? PdfImageFormat.Jpeg
            : PdfImageFormat.Png;
    }

    private int GetPdfDpi()
    {
        var value = GetComboText(_pdfDpiCombo);
        if (value.StartsWith("150", StringComparison.Ordinal))
        {
            return 150;
        }

        if (value.StartsWith("300", StringComparison.Ordinal))
        {
            return 300;
        }

        if (value.StartsWith("400", StringComparison.Ordinal))
        {
            return 400;
        }

        if (value.StartsWith("600", StringComparison.Ordinal))
        {
            return 600;
        }

        return 200;
    }

    private ImagePageMode GetImagePageMode()
    {
        var value = GetComboText(_imagePageModeCombo);
        if (value.Contains("横", StringComparison.Ordinal))
        {
            return ImagePageMode.A4Landscape;
        }

        if (value.Contains("画像", StringComparison.Ordinal))
        {
            return ImagePageMode.ImageSize;
        }

        return ImagePageMode.A4Portrait;
    }

    private PdfToolOperation GetPdfToolOperation()
    {
        var value = GetComboText(_pdfToolOperationCombo);
        if (value.Contains("分割", StringComparison.Ordinal))
        {
            return PdfToolOperation.Split;
        }

        if (value.Contains("削除", StringComparison.Ordinal))
        {
            return PdfToolOperation.DeletePages;
        }

        return PdfToolOperation.Merge;
    }

    private void UpdatePdfToolOperationUi()
    {
        var operation = GetPdfToolOperation();
        _pdfToolOutputPdfPanel.IsVisible = operation is PdfToolOperation.Merge or PdfToolOperation.DeletePages;
        _pdfToolOutputFolderPanel.IsVisible = operation == PdfToolOperation.Split;
        _pdfDeletePagesPanel.IsVisible = operation == PdfToolOperation.DeletePages;
        _pdfToolOutputPdfLabel.Text = operation == PdfToolOperation.DeletePages
            ? "出力PDF"
            : "結合後のPDF";

        if (operation == PdfToolOperation.Merge
            && Path.GetFileName(_pdfToolOutputPdfTextBox.Text ?? string.Empty) == "deleted_pages.pdf")
        {
            _pdfToolOutputPdfTextBox.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "merged.pdf");
        }
        else if (operation == PdfToolOperation.DeletePages
                 && Path.GetFileName(_pdfToolOutputPdfTextBox.Text ?? string.Empty) == "merged.pdf")
        {
            _pdfToolOutputPdfTextBox.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "deleted_pages.pdf");
        }
    }

    private async void RefreshPdfToolPreview()
    {
        if (_mode != ConversionMode.PdfTools || _pdfPagePreviewThumbnailScroll == null)
        {
            return;
        }

        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;

        _pdfPagePreviews.Clear();
        if (_pdfFiles.Count == 0)
        {
            _pdfPreviewHelpText.Text = "PDFを追加すると、ページの見た目を確認できます。";
            return;
        }

        var operation = GetPdfToolOperation();
        var isDeleteMode = operation == PdfToolOperation.DeletePages;
        _pdfPreviewHelpText.Text = isDeleteMode
            ? "削除したいページにチェックを入れると、ページ番号が自動入力されます。"
            : "結合や分割の前に、ページの見た目と順番を確認できます。";

        try
        {
            var pdfPaths = _pdfFiles.Select(item => item.Path).ToList();
            var previews = await Task.Run(
                () => CreatePdfPagePreviews(pdfPaths, isDeleteMode, token),
                token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            foreach (var preview in previews)
            {
                _pdfPagePreviews.Add(preview);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer preview request has replaced this one.
        }
        catch (Exception ex)
        {
            _pdfPreviewHelpText.Text = FriendlyErrorFormatter.ToUserMessage(ex);
        }
    }

    private void UpdatePdfPreviewDisplay()
    {
        _pdfPagePreviewThumbnailScroll.IsVisible = !_isPdfPreviewListView;
        _pdfPagePreviewListScroll.IsVisible = _isPdfPreviewListView;
        SetClass(_pdfPreviewIconButton, "active", !_isPdfPreviewListView);
        SetClass(_pdfPreviewListButton, "active", _isPdfPreviewListView);
    }

    private static List<PdfPagePreviewItem> CreatePdfPagePreviews(
        IReadOnlyList<string> pdfPaths,
        bool isDeleteMode,
        CancellationToken cancellationToken)
    {
        var previews = new List<PdfPagePreviewItem>();
        var options = new RenderOptions(
            Dpi: 45,
            WithAnnotations: true,
            BackgroundColor: SKColors.White,
            UseTiling: true);

        foreach (var pdfPath in pdfPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = File.OpenRead(pdfPath);
            var pageNumber = 0;

            foreach (var bitmap in Conversion.ToImages(stream, options: options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (bitmap)
                {
                    pageNumber++;
                    previews.Add(new PdfPagePreviewItem(
                        pdfPath,
                        pageNumber,
                        ToAvaloniaBitmap(bitmap),
                        isDeleteMode));
                }
            }
        }

        return previews;
    }

    private static Bitmap ToAvaloniaBitmap(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }

    private static string FormatPageRanges(IReadOnlyList<int> pages)
    {
        if (pages.Count == 0)
        {
            return string.Empty;
        }

        var ranges = new List<string>();
        var start = pages[0];
        var previous = pages[0];

        for (var i = 1; i < pages.Count; i++)
        {
            var page = pages[i];
            if (page == previous + 1)
            {
                previous = page;
                continue;
            }

            ranges.Add(start == previous ? start.ToString() : $"{start}-{previous}");
            start = page;
            previous = page;
        }

        ranges.Add(start == previous ? start.ToString() : $"{start}-{previous}");
        return string.Join(",", ranges);
    }

    private static string GetComboText(ComboBox comboBox)
    {
        return comboBox.SelectedItem is ComboBoxItem item
            ? item.Content?.ToString() ?? string.Empty
            : comboBox.SelectedItem?.ToString() ?? string.Empty;
    }

    private static string EnsurePdfExtension(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? path
            : $"{path}.pdf";
    }

    private static bool IsPdfFile(string path)
    {
        return string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsImageFile(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var closeButton = new Button
        {
            Content = "OK",
            MinWidth = 90,
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        var dialog = new Window
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 18,
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    closeButton
                }
            }
        };

        closeButton.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }
}

internal static class EnumerableExtensions
{
    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> values)
        where T : class
    {
        foreach (var value in values)
        {
            if (value != null)
            {
                yield return value;
            }
        }
    }
}

#pragma warning restore CA1416
