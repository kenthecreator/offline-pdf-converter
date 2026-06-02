using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using OfflinePDFConverter.Models;
using OfflinePDFConverter.Services;
using PDFtoImage;
using SkiaSharp;

namespace OfflinePDFConverter.Views;

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
    private readonly List<PdfTextEditDraft> _pdfTextEdits = new();
    private readonly IPdfToImageService _pdfToImageService = new PdfToImageService();
    private readonly IImageToPdfService _imageToPdfService = new ImageToPdfService();
    private readonly IPdfDocumentService _pdfDocumentService = new PdfDocumentService();
    private Image _appHeaderIcon = null!;
    private Border _themeToggleThumb = null!;
    private TextBlock _themeToggleIcon = null!;
    private Button _pdfModeButton = null!;
    private Button _pdfToolsModeButton = null!;
    private Button _pdfToImageDirectionButton = null!;
    private Button _imageToPdfDirectionButton = null!;
    private Button _pdfToImageDirectionButtonInImagePanel = null!;
    private Button _imageToPdfDirectionButtonInImagePanel = null!;
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
    private StackPanel _pdfSimpleEditPanel = null!;
    private TextBlock _pdfToolOutputPdfLabel = null!;
    private TextBox _pdfOutputFolderTextBox = null!;
    private TextBox _pdfOutputBaseNameTextBox = null!;
    private TextBox _imageOutputPdfTextBox = null!;
    private TextBox _imageOutputBaseNameTextBox = null!;
    private TextBox _pdfToolOutputPdfTextBox = null!;
    private TextBox _pdfToolOutputPdfBaseNameTextBox = null!;
    private TextBox _pdfToolOutputFolderTextBox = null!;
    private TextBox _pdfToolOutputBaseNameTextBox = null!;
    private TextBox _pdfDeletePagesTextBox = null!;
    private TextBlock _pdfPreviewHelpText = null!;
    private Button _startPdfButton = null!;
    private Button _startImageButton = null!;
    private Button _startPdfToolButton = null!;
    private ProgressBar _mainProgressBar = null!;
    private TextBlock _statusText = null!;
    private ConversionMode _mode = ConversionMode.PdfTools;
    private CancellationTokenSource? _conversionCts;
    private CancellationTokenSource? _previewCts;
    private bool _isPdfPreviewListView;
    private bool _isDarkTheme;
    private readonly Bitmap _lightHeaderIcon;
    private readonly Bitmap _darkHeaderIcon;

    public MainWindow()
    {
        InitializeComponent();
        _lightHeaderIcon = LoadAssetBitmap("avares://OfflinePDFConverter/Assets/AppIconLight.png");
        _darkHeaderIcon = LoadAssetBitmap("avares://OfflinePDFConverter/Assets/AppIconDark.png");
        BindControls();
        ApplyTheme(isDarkTheme: false);

        _pdfFilesList.ItemsSource = _pdfFiles;
        _pdfToolFilesList.ItemsSource = _pdfFiles;
        _pdfPagePreviewThumbnailItems.ItemsSource = _pdfPagePreviews;
        _pdfPagePreviewListItems.ItemsSource = _pdfPagePreviews;
        _imageFilesList.ItemsSource = _imageFiles;

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        _pdfOutputFolderTextBox.Text = desktop;
        _pdfOutputBaseNameTextBox.Text = "converted";
        _imageOutputPdfTextBox.Text = Path.Combine(desktop, "converted_images.pdf");
        _imageOutputBaseNameTextBox.Text = "converted_images";
        _pdfToolOutputFolderTextBox.Text = desktop;
        _pdfToolOutputBaseNameTextBox.Text = "split";
        _pdfToolOutputPdfTextBox.Text = Path.Combine(desktop, "merged.pdf");
        _pdfToolOutputPdfBaseNameTextBox.Text = "merged";
        UpdatePdfToolOperationUi();
        SetMode(ConversionMode.PdfTools);

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void BindControls()
    {
        _appHeaderIcon = Required<Image>("AppHeaderIcon");
        _themeToggleThumb = Required<Border>("ThemeToggleThumb");
        _themeToggleIcon = Required<TextBlock>("ThemeToggleIcon");
        _pdfModeButton = Required<Button>("PdfModeButton");
        _pdfToolsModeButton = Required<Button>("PdfToolsModeButton");
        _pdfToImageDirectionButton = Required<Button>("PdfToImageDirectionButton");
        _imageToPdfDirectionButton = Required<Button>("ImageToPdfDirectionButton");
        _pdfToImageDirectionButtonInImagePanel = Required<Button>("PdfToImageDirectionButtonInImagePanel");
        _imageToPdfDirectionButtonInImagePanel = Required<Button>("ImageToPdfDirectionButtonInImagePanel");
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
        _pdfSimpleEditPanel = Required<StackPanel>("PdfSimpleEditPanel");
        _pdfToolOutputPdfLabel = Required<TextBlock>("PdfToolOutputPdfLabel");
        _pdfOutputFolderTextBox = Required<TextBox>("PdfOutputFolderTextBox");
        _pdfOutputBaseNameTextBox = Required<TextBox>("PdfOutputBaseNameTextBox");
        _imageOutputPdfTextBox = Required<TextBox>("ImageOutputPdfTextBox");
        _imageOutputBaseNameTextBox = Required<TextBox>("ImageOutputBaseNameTextBox");
        _pdfToolOutputPdfTextBox = Required<TextBox>("PdfToolOutputPdfTextBox");
        _pdfToolOutputPdfBaseNameTextBox = Required<TextBox>("PdfToolOutputPdfBaseNameTextBox");
        _pdfToolOutputFolderTextBox = Required<TextBox>("PdfToolOutputFolderTextBox");
        _pdfToolOutputBaseNameTextBox = Required<TextBox>("PdfToolOutputBaseNameTextBox");
        _pdfDeletePagesTextBox = Required<TextBox>("PdfDeletePagesTextBox");
        _pdfPreviewHelpText = Required<TextBlock>("PdfPreviewHelpText");
        _startPdfButton = Required<Button>("StartPdfButton");
        _startImageButton = Required<Button>("StartImageButton");
        _startPdfToolButton = Required<Button>("StartPdfToolButton");
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

    private void OnPdfToImageDirectionClick(object? sender, RoutedEventArgs e)
    {
        SetMode(ConversionMode.PdfToImage);
    }

    private void OnImageToPdfDirectionClick(object? sender, RoutedEventArgs e)
    {
        SetMode(ConversionMode.ImageToPdf);
    }

    private void OnPdfToolsModeClick(object? sender, RoutedEventArgs e)
    {
        SetMode(ConversionMode.PdfTools);
    }

    private void OnThemeSwitchPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ApplyTheme(!_isDarkTheme);
    }

    private void ApplyTheme(bool isDarkTheme)
    {
        _isDarkTheme = isDarkTheme;
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = isDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        Resources["GlassShellBrush"] = isDarkTheme
            ? Gradient(("#6034373D", 0), ("#4624262C", 0.5), ("#36202227", 1))
            : Gradient(("#F9FFFFFF", 0), ("#DDF7FAFF", 0.5), ("#CCEAF1FF", 1));
        Resources["GlassPanelBrush"] = isDarkTheme
            ? Gradient(("#742A2D33", 0), ("#561A1C22", 0.46), ("#681F2630", 1))
            : Gradient(("#EAF7FAFF", 0), ("#D8FFFFFF", 0.46), ("#CFEAF3FF", 1));
        Resources["GlassCardBrush"] = isDarkTheme
            ? Gradient(("#802E3036", 0), ("#6622262D", 0.55), ("#5A191C23", 1))
            : Gradient(("#F2FFFFFF", 0), ("#E4F5F8FC", 0.55), ("#D8EEF4FA", 1));
        Resources["GlassButtonBrush"] = isDarkTheme
            ? Gradient(("#7A3A3C43", 0), ("#56272A31", 0.58), ("#44202128", 1))
            : Gradient(("#F4FFFFFF", 0), ("#DDE9EEF5", 0.58), ("#CCDDE6F1", 1));
        Resources["GlassAccentBrush"] = isDarkTheme
            ? Gradient(("#FF66C7FF", 0), ("#E6258DFF", 0.55), ("#C90E5FE3", 1))
            : Gradient(("#FF4CC3FF", 0), ("#EA1F91F2", 0.55), ("#D4116ED5", 1));
        Resources["GlassDarkPillBrush"] = isDarkTheme
            ? Gradient(("#8A3B414B", 0), ("#66262A32", 1))
            : Gradient(("#FFEFF4FA", 0), ("#D8DDE7F0", 1));
        Resources["AppBackgroundBrush"] = isDarkTheme
            ? Gradient(("#101113", 0), ("#15171A", 0.48), ("#0B0C0E", 1))
            : Gradient(("#F8FBFF", 0), ("#EEF4FA", 0.48), ("#E5EDF5", 1));

        Resources["TextPrimaryBrush"] = Brush(isDarkTheme ? "#F1F3F6" : "#18202B");
        Resources["TextStrongBrush"] = Brush(isDarkTheme ? "#F4F5F7" : "#0F1722");
        Resources["TextMutedBrush"] = Brush(isDarkTheme ? "#A9AFB8" : "#516071");
        Resources["TextSubtleBrush"] = Brush(isDarkTheme ? "#8D949E" : "#6E7B8D");
        Resources["FieldBackgroundBrush"] = Brush(isDarkTheme ? "#70282B32" : "#EFFFFFFF");
        Resources["FieldBorderBrush"] = Brush(isDarkTheme ? "#35FFFFFF" : "#668596A8");
        Resources["PanelBorderBrush"] = Brush(isDarkTheme ? "#30FFFFFF" : "#668B9AAA");
        Resources["CardBorderBrush"] = Brush(isDarkTheme ? "#28FFFFFF" : "#557F90A4");
        Resources["ButtonBorderBrush"] = Brush(isDarkTheme ? "#36FFFFFF" : "#667C8EA1");
        Resources["ModeButtonBorderBrush"] = Brush(isDarkTheme ? "#3EFFFFFF" : "#6C798B9D");
        Resources["AccentBorderBrush"] = Brush(isDarkTheme ? "#8BE9FFFF" : "#9047BFFF");
        Resources["ThemeTrackBrush"] = Brush(isDarkTheme ? "#D8DDE7F0" : "#A05A5D62");
        Resources["ThemeThumbBrush"] = Brush(isDarkTheme ? "#050505" : "#FFFFFF");
        Resources["ThemeIconBrush"] = Brush(isDarkTheme ? "#FFFFFF" : "#050505");

        _themeToggleThumb.Margin = new Thickness(isDarkTheme ? 44 : 0, 0, 0, 0);
        _themeToggleIcon.Text = isDarkTheme ? "☾" : "☀";
        _appHeaderIcon.Source = isDarkTheme ? _darkHeaderIcon : _lightHeaderIcon;
    }

    private static Bitmap LoadAssetBitmap(string uri)
    {
        return new Bitmap(AssetLoader.Open(new Uri(uri)));
    }

    private static Avalonia.Media.SolidColorBrush Brush(string color)
    {
        return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(color));
    }

    private static Avalonia.Media.LinearGradientBrush Gradient(params (string Color, double Offset)[] stops)
    {
        var brush = new Avalonia.Media.LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative)
        };

        foreach (var (color, offset) in stops)
        {
            brush.GradientStops.Add(new Avalonia.Media.GradientStop(
                Avalonia.Media.Color.Parse(color),
                offset));
        }

        return brush;
    }

    private void SetMode(ConversionMode mode)
    {
        _mode = mode;
        _pdfPanel.IsVisible = mode == ConversionMode.PdfToImage;
        _imagePanel.IsVisible = mode == ConversionMode.ImageToPdf;
        _pdfToolsPanel.IsVisible = mode == ConversionMode.PdfTools;
        _startPdfButton.IsVisible = mode == ConversionMode.PdfToImage;
        _startImageButton.IsVisible = mode == ConversionMode.ImageToPdf;
        _startPdfToolButton.IsVisible = mode == ConversionMode.PdfTools;
        SetClass(_pdfModeButton, "active", mode is ConversionMode.PdfToImage or ConversionMode.ImageToPdf);
        SetClass(_pdfToolsModeButton, "active", mode == ConversionMode.PdfTools);
        SetClass(_pdfToImageDirectionButton, "active", mode == ConversionMode.PdfToImage);
        SetClass(_imageToPdfDirectionButton, "active", mode == ConversionMode.ImageToPdf);
        SetClass(_pdfToImageDirectionButtonInImagePanel, "active", mode == ConversionMode.PdfToImage);
        SetClass(_imageToPdfDirectionButtonInImagePanel, "active", mode == ConversionMode.ImageToPdf);
        SetStatus(null);

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
            _imageOutputBaseNameTextBox.Text = Path.GetFileNameWithoutExtension(_imageOutputPdfTextBox.Text);
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
            _pdfToolOutputPdfBaseNameTextBox.Text = Path.GetFileNameWithoutExtension(_pdfToolOutputPdfTextBox.Text);
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
        _pdfTextEdits.Clear();
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

    private void OnPdfPreviewImagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (GetPdfToolOperation() != PdfToolOperation.SimpleEdit
            || sender is not Image image
            || image.DataContext is not PdfPagePreviewItem item)
        {
            return;
        }

        _ = OpenSimpleEditWindowAsync(item);
    }

    private async void OnStartPdfConversionClick(object? sender, RoutedEventArgs e)
    {
        var request = new PdfToImageRequest(
            _pdfFiles.Select(item => item.Path).ToList(),
            _pdfOutputFolderTextBox.Text?.Trim() ?? string.Empty,
            _pdfOutputBaseNameTextBox.Text?.Trim() ?? string.Empty,
            GetPdfImageFormat(),
            GetPdfDpi());

        await RunConversionAsync(
            (progress, token) => _pdfToImageService.ConvertAsync(request, progress, token),
            "PDFから画像への変換が完了しました。");
    }

    private async void OnStartImageConversionClick(object? sender, RoutedEventArgs e)
    {
        var outputPdfPath = BuildOutputPdfPath(
            _imageOutputPdfTextBox.Text?.Trim() ?? string.Empty,
            _imageOutputBaseNameTextBox.Text?.Trim() ?? string.Empty);
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
            case PdfToolOperation.SimpleEdit:
                await StartSimpleEditPdfAsync();
                break;
        }
    }

    private async Task StartMergePdfAsync()
    {
        var outputPdfPath = BuildOutputPdfPath(
            _pdfToolOutputPdfTextBox.Text?.Trim() ?? string.Empty,
            _pdfToolOutputPdfBaseNameTextBox.Text?.Trim() ?? string.Empty);
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
            _pdfToolOutputFolderTextBox.Text?.Trim() ?? string.Empty,
            _pdfToolOutputBaseNameTextBox.Text?.Trim() ?? string.Empty);

        await RunConversionAsync(
            (progress, token) => _pdfDocumentService.SplitAsync(request, progress, token),
            "PDFの分割が完了しました。");
    }

    private async Task StartDeletePdfPagesAsync()
    {
        var outputPdfPath = BuildOutputPdfPath(
            _pdfToolOutputPdfTextBox.Text?.Trim() ?? string.Empty,
            _pdfToolOutputPdfBaseNameTextBox.Text?.Trim() ?? string.Empty);
        _pdfToolOutputPdfTextBox.Text = outputPdfPath;

        var request = new PdfDeletePagesRequest(
            _pdfFiles.Select(item => item.Path).ToList(),
            _pdfDeletePagesTextBox.Text?.Trim() ?? string.Empty,
            outputPdfPath);

        await RunConversionAsync(
            (progress, token) => _pdfDocumentService.DeletePagesAsync(request, progress, token),
            "指定ページを削除したPDFを作成しました。");
    }

    private async Task StartSimpleEditPdfAsync()
    {
        var outputPdfPath = BuildOutputPdfPath(
            _pdfToolOutputPdfTextBox.Text?.Trim() ?? string.Empty,
            _pdfToolOutputPdfBaseNameTextBox.Text?.Trim() ?? string.Empty);
        _pdfToolOutputPdfTextBox.Text = outputPdfPath;

        var edits = _pdfTextEdits
            .Select(edit => new PdfTextEditItem(
                edit.PageNumber,
                edit.X,
                edit.Y,
                edit.Width,
                edit.Height,
                edit.Text,
                edit.FontFamily,
                edit.FontSize,
                edit.AddWhiteBox,
                edit.BackgroundColorHex,
                edit.TextColorHex,
                edit.TextAlignment))
            .ToList();

        var request = new PdfSimpleEditRequest(
            _pdfFiles.Select(item => item.Path).ToList(),
            edits,
            outputPdfPath);

        await RunConversionAsync(
            (progress, token) => _pdfDocumentService.SimpleEditAsync(request, progress, token),
            "テキストを追加したPDFを作成しました。");
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
                SetStatus(heading);
                await ShowMessageAsync("変換結果", message);
            }
            else
            {
                var message = $"{successMessage}\n\n作成したファイル: {result.CreatedFiles}";
                SetStatus(successMessage);
                await ShowMessageAsync("完了", message);
            }
        }
        catch (OperationCanceledException)
        {
            _mainProgressBar.Value = 0;
            SetStatus("処理を中止しました。");
        }
        catch (Exception ex)
        {
            _mainProgressBar.Value = 0;
            var message = FriendlyErrorFormatter.ToUserMessage(ex);
            SetStatus(message);
            message = $"{message}\n\nテスト版の詳細:\n{ex.GetType().Name}: {ex.Message}";
            if (ex.InnerException != null)
            {
                message += $"\n{ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
            }

            WriteTestErrorLog(ex);

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
        SetStatus(progress.Message);
    }

    private async Task OpenSimpleEditWindowAsync(PdfPagePreviewItem pageItem)
    {
        const double canvasWidth = 560;
        var canvasHeight = Math.Max(360, canvasWidth * pageItem.ThumbnailPixelHeight / pageItem.ThumbnailPixelWidth);
        var pageEdits = new ObservableCollection<PdfTextEditDraft>(
            _pdfTextEdits.Where(edit => edit.PageNumber == pageItem.PageNumber));
        PdfTextEditDraft? selectedEdit = null;
        PdfTextEditDraft? activeEdit = null;
        bool isResizing = false;
        string resizeHandlePosition = "BottomRight";
        bool isLoadingSelection = false;
        TextBox? pickingColorTargetBox = null;
        Button? pickingColorSourceButton = null;
        PdfTextEditDraft? inlineEditingEdit = null;
        Point dragStart = default;
        double startX = 0;
        double startY = 0;
        double startWidth = 0;
        double startHeight = 0;
        var dialogBackground = _isDarkTheme
            ? new SolidColorBrush(Color.Parse("#101113"))
            : new SolidColorBrush(Color.Parse("#EEF4FA"));
        var panelBackground = _isDarkTheme
            ? new SolidColorBrush(Color.Parse("#1D2026"))
            : new SolidColorBrush(Color.Parse("#F8FBFF"));
        var dialogTextBrush = _isDarkTheme
            ? new SolidColorBrush(Color.Parse("#F1F3F6"))
            : new SolidColorBrush(Color.Parse("#18202B"));
        var dialogMutedBrush = _isDarkTheme
            ? new SolidColorBrush(Color.Parse("#B8C0CB"))
            : new SolidColorBrush(Color.Parse("#516071"));
        IBrush dialogFieldBrush = _isDarkTheme
            ? new SolidColorBrush(Color.Parse("#2A2D33"))
            : Brushes.White;
        var dialogFieldBorderBrush = _isDarkTheme
            ? new SolidColorBrush(Color.Parse("#56606C"))
            : new SolidColorBrush(Color.Parse("#B8C4D2"));

        var canvas = new Canvas
        {
            Width = canvasWidth,
            Height = canvasHeight,
            Background = Brushes.White,
            Focusable = true
        };
        canvas.Children.Add(new Image
        {
            Source = pageItem.Thumbnail,
            Width = canvasWidth,
            Height = canvasHeight,
            Stretch = Stretch.Fill
        });

        var textBox = new TextBox
        {
            Watermark = "追加する文字",
            Text = string.Empty,
            Background = dialogFieldBrush,
            Foreground = dialogTextBrush,
            BorderBrush = dialogFieldBorderBrush
        };
        var textColorBox = CreateEditTextBox("#000000");
        var textColorSwatch = new Border
        {
            Width = 40,
            Height = 30,
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Black,
            BorderBrush = new SolidColorBrush(Color.Parse("#8B9AAA")),
            BorderThickness = new Thickness(1)
        };
        var installedFontFamilies = GetInstalledFontFamilyNames();
        var fontFamilyCombo = new ComboBox
        {
            ItemsSource = installedFontFamilies,
            SelectedIndex = 0,
            Background = dialogFieldBrush,
            Foreground = dialogTextBrush,
            BorderBrush = dialogFieldBorderBrush,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var fontSizeBox = CreateEditTextBox("14");
        var widthBox = CreateEditTextBox("160");
        var heightBox = CreateEditTextBox("32");
        var backgroundColorBox = CreateEditTextBox("#FFFFFF");
        var backgroundColorSwatch = new Border
        {
            Width = 42,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse("#8B9AAA")),
            BorderThickness = new Thickness(1)
        };
        var chooseTextColorButton = new Button
        {
            Content = "色を選択",
            Classes = { "small-action" }
        };
        var chooseBackgroundColorButton = new Button
        {
            Content = "色を選択",
            Classes = { "small-action" }
        };
        var addTextBoxButton = new Button
        {
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 10,
                Children =
                {
                    new Border
                    {
                        Width = 34,
                        Height = 34,
                        CornerRadius = new CornerRadius(17),
                        Background = Brushes.White,
                        Child = new TextBlock
                        {
                            Text = "+",
                            FontSize = 29,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = new SolidColorBrush(Color.Parse("#238CF5")),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(0, -4, 0, 0)
                        }
                    },
                    new TextBlock
                    {
                        Text = "テキストボックスを追加",
                        FontSize = 15,
                        FontWeight = FontWeight.Bold,
                        Foreground = Brushes.White,
                        TextTrimming = TextTrimming.None,
                        VerticalAlignment = VerticalAlignment.Center,
                        [Grid.ColumnProperty] = 1
                    }
                }
            },
            MinHeight = 52,
            Padding = new Thickness(12, 8),
            CornerRadius = new CornerRadius(26),
            Background = new SolidColorBrush(Color.Parse("#4AA3F5")),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse("#69B8FF")),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var whiteBox = new CheckBox
        {
            Content = "背景色で塗ってから文字を追加",
            IsChecked = true,
            Foreground = dialogTextBrush
        };
        TextBox CreateEditTextBox(string text)
        {
            return new TextBox
            {
                Text = text,
                Background = dialogFieldBrush,
                Foreground = dialogTextBrush,
                BorderBrush = dialogFieldBorderBrush
            };
        }

        TextBlock Label(string text, double fontSize = 14, FontWeight? weight = null)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = weight ?? FontWeight.Normal,
                Foreground = dialogTextBrush,
                TextWrapping = TextWrapping.Wrap
            };
        }

        string SelectedFontFamily()
        {
            return fontFamilyCombo.SelectedItem?.ToString() ?? installedFontFamilies[0];
        }

        string FontFamilyDisplayName(string? fontFamily)
        {
            if (string.IsNullOrWhiteSpace(fontFamily))
            {
                return installedFontFamilies[0];
            }

            return installedFontFamilies.Contains(fontFamily, StringComparer.OrdinalIgnoreCase)
                ? fontFamily
                : installedFontFamilies[0];
        }

        FontFamily PreviewFontFamily(string? fontFamily)
        {
            return new FontFamily(FontFamilyDisplayName(fontFamily));
        }

        var fontSizeStepper = MacStepper(fontSizeBox, 1, " pt");
        Grid MacStepper(TextBox box, double step, string suffix = "")
        {
            var upButton = new Button
            {
                Content = "⌃",
                MinWidth = 34,
                MinHeight = 20,
                Padding = new Thickness(0),
                Background = dialogFieldBrush,
                Foreground = dialogTextBrush,
                BorderBrush = dialogFieldBorderBrush
            };
            var downButton = new Button
            {
                Content = "⌄",
                MinWidth = 34,
                MinHeight = 20,
                Padding = new Thickness(0),
                Background = dialogFieldBrush,
                Foreground = dialogTextBrush,
                BorderBrush = dialogFieldBorderBrush
            };
            var suffixText = new TextBlock
            {
                Text = suffix,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = dialogTextBrush,
                FontWeight = FontWeight.SemiBold,
                IsVisible = !string.IsNullOrEmpty(suffix)
            };
            upButton.Click += (_, _) => AdjustNumber(box, step);
            downButton.Click += (_, _) => AdjustNumber(box, -step);
            Grid.SetRowSpan(box, 2);
            Grid.SetColumn(suffixText, 1);
            Grid.SetRowSpan(suffixText, 2);
            Grid.SetColumn(upButton, 2);
            Grid.SetColumn(downButton, 2);
            Grid.SetRow(downButton, 1);
            return new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                RowDefinitions = new RowDefinitions("*,*"),
                ColumnSpacing = 4,
                Background = dialogFieldBrush,
                Children =
                {
                    box,
                    suffixText,
                    upButton,
                    downButton
                }
            };
        }

        Grid.SetColumn(fontSizeBox, 0);
        void UpdateSwatch()
        {
            backgroundColorSwatch.Background = TryParseColor(backgroundColorBox.Text, out var color)
                ? new SolidColorBrush(color)
                : Brushes.White;
            textColorSwatch.Background = TryParseColor(textColorBox.Text, out var textColor)
                ? new SolidColorBrush(textColor)
                : Brushes.Black;
        }

        void LoadSelectedEdit(PdfTextEditDraft? edit)
        {
            isLoadingSelection = true;
            try
            {
                if (edit == null)
                {
                    return;
                }

                textBox.Text = edit.Text;
                fontFamilyCombo.SelectedItem = FontFamilyDisplayName(edit.FontFamily);
                fontSizeBox.Text = edit.FontSize.ToString("0.#");
                widthBox.Text = edit.Width.ToString("0.#");
                heightBox.Text = edit.Height.ToString("0.#");
                backgroundColorBox.Text = edit.BackgroundColorHex;
                textColorBox.Text = edit.TextColorHex;
                whiteBox.IsChecked = edit.AddWhiteBox;
                UpdateSwatch();
            }
            finally
            {
                isLoadingSelection = false;
            }
        }

        void ApplyFieldsToSelected()
        {
            if (isLoadingSelection || selectedEdit == null)
            {
                return;
            }

            selectedEdit.Text = textBox.Text?.Trim() ?? string.Empty;
            selectedEdit.FontFamily = SelectedFontFamily();
            if (double.TryParse(fontSizeBox.Text, out var fontSize) && fontSize > 0)
            {
                selectedEdit.FontSize = fontSize;
            }

            if (double.TryParse(widthBox.Text, out var width) && width > 0)
            {
                selectedEdit.Width = width;
            }

            if (double.TryParse(heightBox.Text, out var height) && height > 0)
            {
                selectedEdit.Height = height;
            }

            selectedEdit.BackgroundColorHex = NormalizeColorHex(backgroundColorBox.Text);
            selectedEdit.TextColorHex = NormalizeColorHex(textColorBox.Text);
            selectedEdit.AddWhiteBox = whiteBox.IsChecked == true;
            UpdateSwatch();
            RefreshOverlays();
        }

        void DeleteSelectedEdit(PdfTextEditDraft? editToDelete = null)
        {
            var target = editToDelete ?? selectedEdit;
            if (target == null)
            {
                return;
            }

            pageEdits.Remove(target);
            _pdfTextEdits.Remove(target);
            selectedEdit = null;
            inlineEditingEdit = null;
            textBox.Text = string.Empty;
            RefreshOverlays();
        }

        textBox.TextChanged += (_, _) => ApplyFieldsToSelected();
        fontFamilyCombo.SelectionChanged += (_, _) => ApplyFieldsToSelected();
        fontSizeBox.TextChanged += (_, _) => ApplyFieldsToSelected();
        widthBox.TextChanged += (_, _) => ApplyFieldsToSelected();
        heightBox.TextChanged += (_, _) => ApplyFieldsToSelected();
        backgroundColorBox.TextChanged += (_, _) => ApplyFieldsToSelected();
        textColorBox.TextChanged += (_, _) => ApplyFieldsToSelected();
        whiteBox.IsCheckedChanged += (_, _) => ApplyFieldsToSelected();
        PdfTextEditDraft CreateTextEdit(double pdfX, double pdfY)
        {
            return new PdfTextEditDraft
            {
                PageNumber = pageItem.PageNumber,
                X = Math.Round(Math.Clamp(pdfX, 0, pageItem.PageWidthPoints), 1),
                Y = Math.Round(Math.Clamp(pdfY, 0, pageItem.PageHeightPoints), 1),
                Width = ParsePositiveDouble(widthBox.Text, "幅"),
                Height = ParsePositiveDouble(heightBox.Text, "高さ"),
                Text = textBox.Text?.Trim() ?? string.Empty,
                FontFamily = SelectedFontFamily(),
                FontSize = ParsePositiveDouble(fontSizeBox.Text, "文字サイズ"),
                AddWhiteBox = whiteBox.IsChecked == true,
                BackgroundColorHex = NormalizeColorHex(backgroundColorBox.Text),
                TextColorHex = NormalizeColorHex(textColorBox.Text),
                TextAlignment = "Left"
            };
        }

        void AddTextBoxAt(double pdfX, double pdfY)
        {
            var edit = CreateTextEdit(pdfX, pdfY);
            edit.X = Math.Clamp(edit.X, 0, Math.Max(0, pageItem.PageWidthPoints - edit.Width));
            edit.Y = Math.Clamp(edit.Y, 0, Math.Max(0, pageItem.PageHeightPoints - edit.Height));
            pageEdits.Add(edit);
            selectedEdit = edit;
            LoadSelectedEdit(edit);
            RefreshOverlays();
            canvas.Focus();
        }

        addTextBoxButton.Click += (_, _) =>
        {
            var width = ParsePositiveDouble(widthBox.Text, "幅");
            var height = ParsePositiveDouble(heightBox.Text, "高さ");
            AddTextBoxAt(
                (pageItem.PageWidthPoints - width) / 2,
                (pageItem.PageHeightPoints - height) / 2);
        };

        void RefreshOverlays()
        {
            while (canvas.Children.Count > 1)
            {
                canvas.Children.RemoveAt(1);
            }

            foreach (var edit in pageEdits)
            {
                var left = edit.X / pageItem.PageWidthPoints * canvasWidth;
                var top = edit.Y / pageItem.PageHeightPoints * canvasHeight;
                var width = edit.Width / pageItem.PageWidthPoints * canvasWidth;
                var height = edit.Height / pageItem.PageHeightPoints * canvasHeight;
                IBrush previewTextBrush = TryParseColor(edit.TextColorHex, out var previewTextColor)
                    ? new SolidColorBrush(previewTextColor)
                    : Brushes.Black;
                var previewTextAlignment = edit.TextAlignment switch
                {
                    "Center" => TextAlignment.Center,
                    "Right" => TextAlignment.Right,
                    "Justify" => TextAlignment.Justify,
                    _ => TextAlignment.Left
                };
                Control textControl;
                if (inlineEditingEdit == edit)
                {
                    textControl = new TextBlock
                    {
                        Text = $"{edit.Text}|",
                        FontSize = Math.Max(8, edit.FontSize * canvasWidth / pageItem.PageWidthPoints),
                        FontFamily = PreviewFontFamily(edit.FontFamily),
                        Foreground = previewTextBrush,
                        TextAlignment = previewTextAlignment,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(3, 1, 12, 8)
                    };
                }
                else
                {
                    textControl = new TextBlock
                    {
                        Text = edit.Text,
                        FontSize = Math.Max(8, edit.FontSize * canvasWidth / pageItem.PageWidthPoints),
                        FontFamily = PreviewFontFamily(edit.FontFamily),
                        Foreground = previewTextBrush,
                        TextAlignment = previewTextAlignment,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(3, 1, 12, 8)
                    };
                }

                Border ResizeHandle(string position, HorizontalAlignment horizontal, VerticalAlignment vertical)
                {
                    var handle = new Border
                    {
                        Width = 8,
                        Height = 8,
                        Background = Brushes.White,
                        BorderBrush = new SolidColorBrush(Color.Parse("#111827")),
                        BorderThickness = new Thickness(1),
                        HorizontalAlignment = horizontal,
                        VerticalAlignment = vertical,
                        Margin = new Thickness(-4),
                        Cursor = new Cursor(StandardCursorType.BottomRightCorner),
                        IsVisible = selectedEdit == edit
                    };

                    handle.PointerPressed += (_, e) =>
                    {
                        selectedEdit = edit;
                        activeEdit = edit;
                        isResizing = true;
                        resizeHandlePosition = position;
                        dragStart = e.GetPosition(canvas);
                        startX = edit.X;
                        startY = edit.Y;
                        startWidth = edit.Width;
                        startHeight = edit.Height;
                        e.Pointer.Capture(canvas);
                        e.Handled = true;
                    };

                    return handle;
                }

                var topLeftHandle = ResizeHandle("TopLeft", HorizontalAlignment.Left, VerticalAlignment.Top);
                var topHandle = ResizeHandle("Top", HorizontalAlignment.Center, VerticalAlignment.Top);
                var topRightHandle = ResizeHandle("TopRight", HorizontalAlignment.Right, VerticalAlignment.Top);
                var leftHandle = ResizeHandle("Left", HorizontalAlignment.Left, VerticalAlignment.Center);
                var rightHandle = ResizeHandle("Right", HorizontalAlignment.Right, VerticalAlignment.Center);
                var bottomLeftHandle = ResizeHandle("BottomLeft", HorizontalAlignment.Left, VerticalAlignment.Bottom);
                var bottomHandle = ResizeHandle("Bottom", HorizontalAlignment.Center, VerticalAlignment.Bottom);
                var bottomRightHandle = ResizeHandle("BottomRight", HorizontalAlignment.Right, VerticalAlignment.Bottom);
                var inlineDeleteButton = new Button
                {
                    Content = "削除",
                    FontSize = 12,
                    Padding = new Thickness(8, 3),
                    MinHeight = 24,
                    Background = new SolidColorBrush(Color.Parse("#D92D20")),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.Parse("#FCA5A5")),
                    IsVisible = selectedEdit == edit
                };
                inlineDeleteButton.AddHandler(
                    PointerPressedEvent,
                    (_, e) =>
                    {
                        e.Handled = true;
                    },
                    RoutingStrategies.Tunnel);
                inlineDeleteButton.PointerReleased += (_, e) =>
                {
                    DeleteSelectedEdit(edit);
                    e.Handled = true;
                };
                inlineDeleteButton.Click += (_, e) =>
                {
                    DeleteSelectedEdit(edit);
                    e.Handled = true;
                };
                var box = new Border
                {
                    Width = Math.Max(24, width),
                    Height = Math.Max(18, height),
                    Background = edit.AddWhiteBox && TryParseColor(edit.BackgroundColorHex, out var bgColor)
                        ? new SolidColorBrush(bgColor)
                        : Brushes.Transparent,
                    BorderBrush = selectedEdit == edit
                        ? new SolidColorBrush(Color.Parse("#0E5FE3"))
                        : new SolidColorBrush(Color.Parse("#258DFF")),
                    BorderThickness = selectedEdit == edit ? new Thickness(2) : new Thickness(1),
                    Cursor = new Cursor(StandardCursorType.SizeAll),
                    Child = new Grid
                    {
                        Children =
                        {
                            textControl,
                            topLeftHandle,
                            topHandle,
                            topRightHandle,
                            leftHandle,
                            rightHandle,
                            bottomLeftHandle,
                            bottomHandle,
                            bottomRightHandle,
                            inlineDeleteButton
                        }
                    }
                };
                inlineDeleteButton.HorizontalAlignment = HorizontalAlignment.Right;
                inlineDeleteButton.VerticalAlignment = VerticalAlignment.Top;
                box.PointerPressed += (_, e) =>
                {
                    selectedEdit = edit;
                    LoadSelectedEdit(edit);
                    canvas.Focus();
                    if (e.ClickCount >= 2)
                    {
                        inlineEditingEdit = edit;
                        activeEdit = null;
                        canvas.Focus();
                        e.Handled = true;
                        RefreshOverlays();
                        return;
                    }

                    activeEdit = edit;
                    isResizing = false;
                    dragStart = e.GetPosition(canvas);
                    startX = edit.X;
                    startY = edit.Y;
                    startWidth = edit.Width;
                    startHeight = edit.Height;
                    e.Pointer.Capture(canvas);
                    e.Handled = true;
                    RefreshOverlays();
                };
                Canvas.SetLeft(box, left);
                Canvas.SetTop(box, top);
                canvas.Children.Add(box);
            }
        }

        canvas.PointerPressed += (_, e) =>
        {
            canvas.Focus();
            var position = e.GetPosition(canvas);
            if (pickingColorTargetBox != null)
            {
                var picked = PickPreviewColor(pageItem, position.X / canvasWidth, position.Y / canvasHeight);
                pickingColorTargetBox.Text = picked;
                UpdateSwatch();
                ApplyFieldsToSelected();
                if (pickingColorSourceButton != null)
                {
                    pickingColorSourceButton.Content = "色を選択";
                }

                pickingColorTargetBox = null;
                pickingColorSourceButton = null;
                e.Handled = true;
                return;
            }

            selectedEdit = null;
            inlineEditingEdit = null;
            RefreshOverlays();
        };
        canvas.PointerMoved += (_, e) =>
        {
            if (activeEdit == null)
            {
                return;
            }

            var position = e.GetPosition(canvas);
            var deltaX = (position.X - dragStart.X) / canvasWidth * pageItem.PageWidthPoints;
            var deltaY = (position.Y - dragStart.Y) / canvasHeight * pageItem.PageHeightPoints;

            if (isResizing)
            {
                const double minWidth = 8;
                const double minHeight = 8;
                var newX = startX;
                var newY = startY;
                var newWidth = startWidth;
                var newHeight = startHeight;

                if (resizeHandlePosition.Contains("Left", StringComparison.Ordinal))
                {
                    newX = Math.Clamp(startX + deltaX, 0, startX + startWidth - minWidth);
                    newWidth = startWidth + startX - newX;
                }
                else if (resizeHandlePosition.Contains("Right", StringComparison.Ordinal))
                {
                    newWidth = Math.Clamp(startWidth + deltaX, minWidth, pageItem.PageWidthPoints - startX);
                }

                if (resizeHandlePosition.Contains("Top", StringComparison.Ordinal))
                {
                    newY = Math.Clamp(startY + deltaY, 0, startY + startHeight - minHeight);
                    newHeight = startHeight + startY - newY;
                }
                else if (resizeHandlePosition.Contains("Bottom", StringComparison.Ordinal))
                {
                    newHeight = Math.Clamp(startHeight + deltaY, minHeight, pageItem.PageHeightPoints - startY);
                }

                activeEdit.X = Math.Round(newX, 1);
                activeEdit.Y = Math.Round(newY, 1);
                activeEdit.Width = Math.Round(newWidth, 1);
                activeEdit.Height = Math.Round(newHeight, 1);
            }
            else
            {
                activeEdit.X = Math.Clamp(startX + deltaX, 0, Math.Max(0, pageItem.PageWidthPoints - activeEdit.Width));
                activeEdit.Y = Math.Clamp(startY + deltaY, 0, Math.Max(0, pageItem.PageHeightPoints - activeEdit.Height));
            }

            if (selectedEdit == activeEdit)
            {
                isLoadingSelection = true;
                try
                {
                    widthBox.Text = activeEdit.Width.ToString("0.#");
                    heightBox.Text = activeEdit.Height.ToString("0.#");
                }
                finally
                {
                    isLoadingSelection = false;
                }
            }

            RefreshOverlays();
        };
        canvas.PointerReleased += (_, e) =>
        {
            activeEdit = null;
            isResizing = false;
            e.Pointer.Capture(null);
        };
        canvas.KeyDown += (_, e) =>
        {
            // テキストボックス自体の削除は、枠内の「削除」ボタンだけで行う。
        };
        var doneButton = new Button
        {
            Content = "完了",
            Classes = { "primary" },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 28,
            FontWeight = FontWeight.Bold,
            MinHeight = 66,
            Padding = new Thickness(24, 12),
            CornerRadius = new CornerRadius(28),
            Margin = new Thickness(0, 12, 0, 0)
        };
        var textColorRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 8,
            Children =
            {
                textColorSwatch,
                textColorBox,
                chooseTextColorButton
            }
        };
        Grid.SetColumn(textColorBox, 1);
        Grid.SetColumn(chooseTextColorButton, 2);

        var backgroundRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 8,
            Children =
            {
                backgroundColorSwatch,
                backgroundColorBox,
                chooseBackgroundColorButton
            }
        };
        Grid.SetColumn(backgroundColorBox, 1);
        Grid.SetColumn(chooseBackgroundColorButton, 2);

        var settingsPanel = new StackPanel
        {
            Spacing = 12,
            Background = panelBackground,
            Children =
            {
                addTextBoxButton,
                Label("フォント", 15, FontWeight.Bold),
                fontFamilyCombo,
                fontSizeStepper,
                new Border
                {
                    Height = 1,
                    Background = dialogFieldBorderBrush,
                    Margin = new Thickness(0, 4)
                },
                Label("テキスト色", 14, FontWeight.Bold),
                textColorRow,
                whiteBox,
                Label("背景色"),
                backgroundRow
            }
        };
        var rightPanel = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            [Grid.ColumnProperty] = 1,
            Background = panelBackground,
            Children =
            {
                new ScrollViewer
                {
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = settingsPanel
                },
                doneButton
            }
        };
        Grid.SetRow(doneButton, 1);

        var dialog = new Window
        {
            Title = $"{pageItem.PageLabel}を編集",
            Width = 900,
            Height = 720,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = dialogBackground,
            Content = new Grid
            {
                Margin = new Thickness(20),
                ColumnDefinitions = new ColumnDefinitions("*,260"),
                ColumnSpacing = 18,
                Children =
                {
                    new ScrollViewer
                    {
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        Content = canvas
                    },
                    rightPanel
                }
            }
        };

        async Task OpenColorChooserAsync(string title, TextBox targetBox, Button sourceButton)
        {
            var chooser = new Window
            {
                Title = title,
                Width = 360,
                Height = 330,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Background = dialogBackground
            };
            var palette = new WrapPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                ItemWidth = 34,
                ItemHeight = 34
            };
            var colorChoices = new[]
            {
                "#000000", "#404040", "#808080", "#C0C0C0", "#FFFFFF",
                "#FF3B30", "#FF9500", "#FFCC00", "#34C759", "#00C7BE",
                "#32ADE6", "#007AFF", "#5856D6", "#AF52DE", "#FF2D55",
                "#8E8E93", "#D1D1D6", "#F2F2F7", "#E6F4FF", "#FFF3CD",
                "#FCE4EC", "#E8F5E9", "#EDE7F6", "#E0F7FA", "#F5F5F5"
            };

            foreach (var hex in colorChoices)
            {
                var swatch = new Button
                {
                    Width = 30,
                    Height = 30,
                    MinWidth = 30,
                    MinHeight = 30,
                    Padding = new Thickness(0),
                    Margin = new Thickness(3),
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Color.Parse(hex)),
                    BorderBrush = new SolidColorBrush(Color.Parse("#8B9AAA")),
                    BorderThickness = new Thickness(1)
                };
                swatch.Click += (_, _) =>
                {
                    targetBox.Text = hex;
                    UpdateSwatch();
                    ApplyFieldsToSelected();
                    chooser.Close();
                };
                palette.Children.Add(swatch);
            }

            var pickFromPdfButton = new Button
            {
                Content = "PDFから色を拾う",
                Classes = { "small-action" },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 10, 0, 0)
            };
            pickFromPdfButton.Click += (_, _) =>
            {
                pickingColorTargetBox = targetBox;
                pickingColorSourceButton = sourceButton;
                sourceButton.Content = "ページ上をクリック";
                chooser.Close();
            };

            chooser.Content = new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 12,
                Children =
                {
                    Label(title, 18, FontWeight.Bold),
                    new TextBlock
                    {
                        Text = "色を選ぶか、PDF上の色を拾って設定できます。",
                        Foreground = dialogMutedBrush,
                        TextWrapping = TextWrapping.Wrap
                    },
                    palette,
                    pickFromPdfButton
                }
            };

            await chooser.ShowDialog(dialog);
        }

        chooseTextColorButton.Click += async (_, _) =>
            await OpenColorChooserAsync("テキスト色", textColorBox, chooseTextColorButton);
        chooseBackgroundColorButton.Click += async (_, _) =>
            await OpenColorChooserAsync("背景色", backgroundColorBox, chooseBackgroundColorButton);

        dialog.TextInput += (_, e) =>
        {
            if (inlineEditingEdit == null || string.IsNullOrEmpty(e.Text))
            {
                return;
            }

            inlineEditingEdit.Text += e.Text;
            if (selectedEdit == inlineEditingEdit)
            {
                isLoadingSelection = true;
                try
                {
                    textBox.Text = inlineEditingEdit.Text;
                }
                finally
                {
                    isLoadingSelection = false;
                }
            }

            e.Handled = true;
            RefreshOverlays();
        };
        dialog.KeyDown += (_, e) =>
        {
            if (inlineEditingEdit != null)
            {
                if (e.Key == Key.Enter)
                {
                    inlineEditingEdit = null;
                    e.Handled = true;
                    RefreshOverlays();
                    return;
                }

                if (e.Key == Key.Escape)
                {
                    inlineEditingEdit = null;
                    e.Handled = true;
                    RefreshOverlays();
                    return;
                }

                if (e.Key == Key.Back && inlineEditingEdit.Text.Length > 0)
                {
                    inlineEditingEdit.Text = inlineEditingEdit.Text[..^1];
                    if (selectedEdit == inlineEditingEdit)
                    {
                        isLoadingSelection = true;
                        try
                        {
                            textBox.Text = inlineEditingEdit.Text;
                        }
                        finally
                        {
                            isLoadingSelection = false;
                        }
                    }

                    e.Handled = true;
                    RefreshOverlays();
                    return;
                }
            }
        };

        Grid.SetColumn(backgroundColorBox, 1);
        doneButton.Click += (_, _) => dialog.Close();
        RefreshOverlays();
        await dialog.ShowDialog(this);

        _pdfTextEdits.RemoveAll(edit => edit.PageNumber == pageItem.PageNumber);
        _pdfTextEdits.AddRange(pageEdits);
        SetStatus($"{pageItem.PageLabel}の編集を一時保存しました。全ページの編集後、開始で書き出します。");
    }

    private void SetBusy(bool busy)
    {
        _startPdfButton.IsEnabled = !busy;
        _startImageButton.IsEnabled = !busy;
        _startPdfToolButton.IsEnabled = !busy;
        _pdfModeButton.IsEnabled = !busy;
        _pdfToolsModeButton.IsEnabled = !busy;
        _pdfToImageDirectionButton.IsEnabled = !busy;
        _imageToPdfDirectionButton.IsEnabled = !busy;
        _pdfToImageDirectionButtonInImagePanel.IsEnabled = !busy;
        _imageToPdfDirectionButtonInImagePanel.IsEnabled = !busy;
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
        SetStatus(_mode == ConversionMode.PdfTools
            ? $"{_pdfFiles.Count}件のPDFが選択されています。結合時は一覧の順番どおりに並びます。"
            : $"{_pdfFiles.Count}件のPDFが選択されています。");

        RefreshPdfToolPreview();
    }

    private void AddImagePaths(IEnumerable<string> paths)
    {
        AddPaths(paths.Where(IsImageFile), _imageFiles);
        SetStatus($"{_imageFiles.Count}件の画像が選択されています。");
    }

    private void SetStatus(string? message)
    {
        _statusText.Text = message ?? string.Empty;
        _statusText.IsVisible = !string.IsNullOrWhiteSpace(message);
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
        if (value.Contains("超高画質", StringComparison.Ordinal))
        {
            return 600;
        }

        if (value.Contains("高画質", StringComparison.Ordinal))
        {
            return 300;
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

        if (value.Contains("テキスト追加", StringComparison.Ordinal)
            || value.Contains("簡易編集", StringComparison.Ordinal)
            || value.Contains("注釈", StringComparison.Ordinal))
        {
            return PdfToolOperation.SimpleEdit;
        }

        return PdfToolOperation.Merge;
    }

    private void UpdatePdfToolOperationUi()
    {
        var operation = GetPdfToolOperation();
        _pdfToolOutputPdfPanel.IsVisible = operation is PdfToolOperation.Merge or PdfToolOperation.DeletePages or PdfToolOperation.SimpleEdit;
        _pdfToolOutputFolderPanel.IsVisible = operation == PdfToolOperation.Split;
        _pdfDeletePagesPanel.IsVisible = operation == PdfToolOperation.DeletePages;
        _pdfSimpleEditPanel.IsVisible = operation == PdfToolOperation.SimpleEdit;
        _pdfToolOutputPdfLabel.Text = operation == PdfToolOperation.DeletePages
            ? "出力PDF"
            : operation == PdfToolOperation.SimpleEdit
                ? "編集後のPDF"
            : "結合後のPDF";

        if (operation == PdfToolOperation.Merge
            && (Path.GetFileName(_pdfToolOutputPdfTextBox.Text ?? string.Empty) == "deleted_pages.pdf"
                || Path.GetFileName(_pdfToolOutputPdfTextBox.Text ?? string.Empty) == "edited.pdf"))
        {
            _pdfToolOutputPdfTextBox.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "merged.pdf");
            _pdfToolOutputPdfBaseNameTextBox.Text = "merged";
        }
        else if (operation == PdfToolOperation.DeletePages
                 && Path.GetFileName(_pdfToolOutputPdfTextBox.Text ?? string.Empty) == "merged.pdf")
        {
            _pdfToolOutputPdfTextBox.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "deleted_pages.pdf");
            _pdfToolOutputPdfBaseNameTextBox.Text = "deleted_pages";
        }
        else if (operation == PdfToolOperation.SimpleEdit
                 && Path.GetFileName(_pdfToolOutputPdfTextBox.Text ?? string.Empty) is "merged.pdf" or "deleted_pages.pdf")
        {
            _pdfToolOutputPdfTextBox.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "edited.pdf");
            _pdfToolOutputPdfBaseNameTextBox.Text = "edited";
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
            : operation == PdfToolOperation.SimpleEdit
                ? "編集したいページ上をクリックすると、右側に位置が自動入力されます。"
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
        var options = new PDFtoImage.RenderOptions(
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
                        bitmap.Width * 72.0 / options.Dpi,
                        bitmap.Height * 72.0 / options.Dpi,
                        CopyBgraPixels(bitmap),
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

    private static byte[] CopyBgraPixels(SKBitmap bitmap)
    {
        var pixels = new byte[bitmap.Width * bitmap.Height * 4];
        var index = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                pixels[index++] = color.Blue;
                pixels[index++] = color.Green;
                pixels[index++] = color.Red;
                pixels[index++] = color.Alpha;
            }
        }

        return pixels;
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

    private static int ParsePositiveInt(string? value, string label)
    {
        if (!int.TryParse(value?.Trim(), out var number) || number <= 0)
        {
            throw new ArgumentException($"{label}は1以上の数字で入力してください。");
        }

        return number;
    }

    private static double ParsePositiveDouble(string? value, string label)
    {
        if (!double.TryParse(value?.Trim(), out var number) || number <= 0)
        {
            throw new ArgumentException($"{label}は1以上の数字で入力してください。");
        }

        return number;
    }

    private static double ParseNonNegativeDouble(string? value, string label)
    {
        if (!double.TryParse(value?.Trim(), out var number) || number < 0)
        {
            throw new ArgumentException($"{label}は0以上の数字で入力してください。");
        }

        return number;
    }

    private static (double PdfX, double PdfY, double MarkerLeft, double MarkerTop)? MapPreviewPointToPdfPoint(
        PdfPagePreviewItem item,
        double controlWidth,
        double controlHeight,
        double positionX,
        double positionY)
    {
        if (item.ThumbnailPixelWidth <= 0
            || item.ThumbnailPixelHeight <= 0
            || controlWidth <= 0
            || controlHeight <= 0)
        {
            return null;
        }

        var scale = Math.Min(controlWidth / item.ThumbnailPixelWidth, controlHeight / item.ThumbnailPixelHeight);
        var drawnWidth = item.ThumbnailPixelWidth * scale;
        var drawnHeight = item.ThumbnailPixelHeight * scale;
        var offsetX = (controlWidth - drawnWidth) / 2;
        var offsetY = (controlHeight - drawnHeight) / 2;
        var imageX = Math.Clamp(positionX - offsetX, 0, drawnWidth);
        var imageY = Math.Clamp(positionY - offsetY, 0, drawnHeight);
        var pdfX = imageX / drawnWidth * item.PageWidthPoints;
        var pdfY = imageY / drawnHeight * item.PageHeightPoints;
        var markerLeft = offsetX + imageX - 6;
        var markerTop = offsetY + imageY - 6;

        return (pdfX, pdfY, markerLeft, markerTop);
    }

    private static void AdjustNumber(TextBox textBox, double delta)
    {
        var current = double.TryParse(textBox.Text, out var value) ? value : 0;
        textBox.Text = Math.Max(1, current + delta).ToString("0.#");
    }

    private static IReadOnlyList<string> GetInstalledFontFamilyNames()
    {
        var names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var preferred = new[] { "Hiragino Sans", "Yu Gothic", "Meiryo", "Arial" };

        try
        {
            foreach (var fontFamily in FontManager.Current.SystemFonts)
            {
                var name = fontFamily.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }
        }
        catch
        {
            // Fall back to common OS fonts if the platform font list is unavailable.
        }

        foreach (var fallback in preferred)
        {
            names.Add(fallback);
        }

        return preferred
            .Where(names.Contains)
            .Concat(names.Where(name => !preferred.Contains(name, StringComparer.CurrentCultureIgnoreCase)))
            .ToList();
    }

    private static bool TryParseColor(string? colorHex, out Color color)
    {
        color = Colors.White;
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            return false;
        }

        try
        {
            color = Color.Parse(NormalizeColorHex(colorHex));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeColorHex(string? colorHex)
    {
        var value = colorHex?.Trim() ?? string.Empty;
        if (!value.StartsWith('#'))
        {
            value = $"#{value}";
        }

        if (value.Length == 7
            && byte.TryParse(value.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out _)
            && byte.TryParse(value.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out _)
            && byte.TryParse(value.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out _))
        {
            return value.ToUpperInvariant();
        }

        return "#FFFFFF";
    }

    private static string PickPreviewColor(PdfPagePreviewItem item, double normalizedX, double normalizedY)
    {
        if (item.BgraPixels.Length == 0 || item.ThumbnailPixelWidth <= 0 || item.ThumbnailPixelHeight <= 0)
        {
            return "#FFFFFF";
        }

        var x = Math.Clamp((int)Math.Round(normalizedX * (item.ThumbnailPixelWidth - 1)), 0, item.ThumbnailPixelWidth - 1);
        var y = Math.Clamp((int)Math.Round(normalizedY * (item.ThumbnailPixelHeight - 1)), 0, item.ThumbnailPixelHeight - 1);
        var index = (y * item.ThumbnailPixelWidth + x) * 4;
        var b = item.BgraPixels[index];
        var g = item.BgraPixels[index + 1];
        var r = item.BgraPixels[index + 2];
        return $"#{r:X2}{g:X2}{b:X2}";
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

    private static string BuildOutputPdfPath(string currentPath, string outputBaseName)
    {
        var path = EnsurePdfExtension(currentPath);
        if (string.IsNullOrWhiteSpace(outputBaseName))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        return Path.Combine(directory, $"{FileNameHelper.SafeBaseName(outputBaseName)}.pdf");
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

    private static void WriteTestErrorLog(Exception ex)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var path = Path.Combine(desktop, "Offline PDF Converter Simple Edit Test Error.txt");
            File.WriteAllText(path, ex.ToString());
        }
        catch
        {
            // エラー調査用ログなので、ログ出力の失敗は画面操作を止めない。
        }
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var closeButton = new Button
        {
            Content = "OK",
            MinWidth = 90,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#70282B32")),
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F1F3F6")),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#36FFFFFF")),
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        var dialog = new Window
        {
            Title = title,
            Width = 520,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1A1C21")),
            Content = new Grid
            {
                Margin = new Avalonia.Thickness(20),
                RowDefinitions = new RowDefinitions("*,Auto"),
                Children =
                {
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        Content = new TextBlock
                        {
                            Text = message,
                            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F1F3F6")),
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        }
                    },
                    closeButton
                }
            }
        };

        Grid.SetRow(closeButton, 1);
        closeButton.Margin = new Thickness(0, 18, 0, 0);
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

internal sealed class PdfTextEditDraft
{
    public int PageNumber { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public string Text { get; set; } = string.Empty;

    public string FontFamily { get; set; } = "OfflinePDFConverterGothic";

    public double FontSize { get; set; }

    public bool AddWhiteBox { get; set; }

    public string BackgroundColorHex { get; set; } = "#FFFFFF";

    public string TextColorHex { get; set; } = "#000000";

    public string TextAlignment { get; set; } = "Left";

    public string DisplayText => string.IsNullOrWhiteSpace(Text) ? "(空のテキスト)" : Text;
}

#pragma warning restore CA1416
