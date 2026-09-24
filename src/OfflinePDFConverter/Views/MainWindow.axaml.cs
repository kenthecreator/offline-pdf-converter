using System.Collections.ObjectModel;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
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
using Avalonia.VisualTree;
using OfflinePDFConverter.Models;
using OfflinePDFConverter.Services;
using PDFtoImage;
using SkiaSharp;
using UglyToad.PdfPig;

namespace OfflinePDFConverter.Views;

#pragma warning disable CA1416 // PDF preview rendering uses PDFtoImage on supported desktop platforms.

public partial class MainWindow : Window
{
    private const double ModeDragMaximum = 208;
    private const double DirectionDragMaximum = 125;
    private const double PreviewViewDragMaximum = 72;
    private const double ThemeDragMaximum = 33;
    private const double PdfDpiDragMaximum = 169;
    private const double PdfDpiTrackInset = 14;
    private const double DragActivationDistance = 4;
    private static readonly string[] PdfDpiLabels = { "普通", "高画質", "超高画質" };
    private static readonly int[] PdfDpiValues = { 200, 300, 600 };
    private static readonly double[] PdfDpiThumbPositions = { 0, 84.5, 169 };
    private static readonly double[] PdfDpiRangeWidths = { 14, 98.5, 183 };
    private const int ThemePaletteDurationMilliseconds = 520;
    private static readonly (string Key, string Light, string Dark)[] ThemePalette =
    {
        ("GlassShellBrush", "#DDF5F5F5", "#46282828"),
        ("GlassPanelBrush", "#D8FFFFFF", "#56202020"),
        ("GlassCardBrush", "#E4F7F7F7", "#66282828"),
        ("GlassButtonBrush", "#DDEDEDED", "#562E2E2E"),
        ("GlassAccentBrush", "#2C2C2C", "#E5E5E5"),
        ("GlassDarkPillBrush", "#ECE7E7E7", "#73383838"),
        ("AppBackgroundBrush", "#F2F2F2", "#191919"),
        ("TextPrimaryBrush", "#202020", "#F2F2F2"),
        ("TextStrongBrush", "#141414", "#F6F6F6"),
        ("TextMutedBrush", "#606060", "#B0B0B0"),
        ("TextSubtleBrush", "#7A7A7A", "#929292"),
        ("FieldBackgroundBrush", "#EFFFFFFF", "#702D2D2D"),
        ("FieldBorderBrush", "#66969696", "#35FFFFFF"),
        ("PanelBorderBrush", "#66999999", "#30FFFFFF"),
        ("CardBorderBrush", "#558F8F8F", "#28FFFFFF"),
        ("ButtonBorderBrush", "#66898989", "#36FFFFFF"),
        ("ModeButtonBorderBrush", "#6C868686", "#3EFFFFFF"),
        ("AccentBorderBrush", "#5C5C5C", "#F5F5F5"),
        ("AccentTextBrush", "#FFFFFF", "#121212"),
        ("ThemeTrackBrush", "#A0606060", "#D8E2E2E2"),
        ("ThemeThumbBrush", "#FFFFFF", "#050505"),
        ("ThemeIconBrush", "#050505", "#FFFFFF"),
        ("ModeTrackBrush", "#08000000", "#111111"),
        ("ModeTrackBorderBrush", "#00000000", "#00000000"),
        ("ModePillBrush", "#FFFFFF", "#3A3A3A"),
        ("ModePillBorderBrush", "#1A000000", "#2EFFFFFF"),
        ("ModeSelectedTextBrush", "#0D0D0D", "#FFFFFF"),
        ("ModeUnselectedTextBrush", "#5D5D5D", "#A6A6A6"),
        ("ComboMenuBackgroundBrush", "#F2F2F2", "#F03B3B3B"),
        ("ComboMenuBorderBrush", "#38000000", "#52FFFFFF"),
        ("ComboMenuHoverBrush", "#12000000", "#20FFFFFF"),
        ("ComboMenuSelectedBrush", "#D8D8D8", "#666666"),
        ("ComboMenuSelectedTextBrush", "#111111", "#FFFFFF"),
        ("PdfDpiTrackBrush", "#10000000", "#20FFFFFF"),
        ("PdfDpiThumbBrush", "#FFFFFFFF", "#FFF2F2F2"),
        ("PdfDpiThumbBorderBrush", "#42000000", "#66000000")
    };

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

    private static readonly FilePickerFileType TextFileType = new("テキストファイル")
    {
        Patterns = new[] { "*.txt" },
        MimeTypes = new[] { "text/plain" }
    };

    private readonly ObservableCollection<FileItem> _pdfFiles = new();
    private readonly ObservableCollection<FileItem> _imageFiles = new();
    private readonly ObservableCollection<PdfPagePreviewItem> _pdfPagePreviews = new();
    private readonly Dictionary<string, string> _pdfPasswords = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string PdfPath, int PageNumber), PdfTextSelectionState> _pdfTextSelections = new();
    private readonly List<PdfTextEditDraft> _pdfTextEdits = new();
    private readonly List<PdfShapeEditDraft> _pdfShapeEdits = new();
    private readonly IPdfToImageService _pdfToImageService = new PdfToImageService();
    private readonly IImageToPdfService _imageToPdfService = new ImageToPdfService();
    private readonly IPdfDocumentService _pdfDocumentService = new PdfDocumentService();
    private readonly IPdfTextExtractionService _pdfTextExtractionService = new PdfTextExtractionService();
    private Image _appHeaderIcon = null!;
    private Grid _themeTransitionRoot = null!;
    private Border _modeSelectorSwitch = null!;
    private Border _themeToggleSwitch = null!;
    private Border _themeToggleThumb = null!;
    private TextBlock _themeLightLabel = null!;
    private TextBlock _themeDarkLabel = null!;
    private TranslateTransform _themeToggleTransform = null!;
    private RadioButton _pdfModeButton = null!;
    private RadioButton _pdfToolsModeButton = null!;
    private TextBlock _pdfModeLabel = null!;
    private TextBlock _pdfToolsModeLabel = null!;
    private TranslateTransform _modeSelectionTransform = null!;
    private Transitions? _modeSelectionTransitions;
    private Transitions? _themeToggleTransitions;
    private bool _modeDragActive;
    private bool _modeDragMoved;
    private double _modeDragStartPointerX;
    private double _modeDragStartTransformX;
    private bool _themeDragActive;
    private bool _themeDragMoved;
    private double _themeDragStartPointerX;
    private double _themeDragStartTransformX;
    private Grid _directionSelectorHost = null!;
    private Border _directionSelectorSwitch = null!;
    private RadioButton _pdfToImageDirectionButton = null!;
    private RadioButton _imageToPdfDirectionButton = null!;
    private TextBlock _pdfToImageDirectionLabel = null!;
    private TextBlock _imageToPdfDirectionLabel = null!;
    private TranslateTransform _directionSelectionTransform = null!;
    private Transitions? _directionSelectionTransitions;
    private bool _directionDragActive;
    private bool _directionDragMoved;
    private double _directionDragStartPointerX;
    private double _directionDragStartTransformX;
    private Grid _pdfPanel = null!;
    private Grid _imagePanel = null!;
    private Grid _pdfToolsPanel = null!;
    private ListBox _pdfFilesList = null!;
    private ListBox _pdfToolFilesList = null!;
    private TextBlock _pdfFilesEmptyHint = null!;
    private TextBlock _pdfToolFilesEmptyHint = null!;
    private ScrollViewer _pdfPagePreviewThumbnailScroll = null!;
    private ScrollViewer _pdfPagePreviewListScroll = null!;
    private ItemsControl _pdfPagePreviewThumbnailItems = null!;
    private ItemsControl _pdfPagePreviewListItems = null!;
    private ListBox _imageFilesList = null!;
    private ComboBox _pdfFormatCombo = null!;
    private Border _pdfDpiSliderSwitch = null!;
    private Border _pdfDpiRange = null!;
    private Border _pdfDpiThumb = null!;
    private TranslateTransform _pdfDpiThumbTransform = null!;
    private DoubleTransition _pdfDpiThumbTransition = null!;
    private DoubleTransition _pdfDpiRangeTransition = null!;
    private TextBlock _pdfDpiCurrentHint = null!;
    private Border[] _pdfDpiTicks = Array.Empty<Border>();
    private bool _pdfDpiDragActive;
    private double _pdfDpiDragVisualX;
    private int _pdfDpiIndex;
    private ComboBox _imagePageModeCombo = null!;
    private CheckBox _imageMarginCheckBox = null!;
    private ComboBox _pdfToolOperationCombo = null!;
    private Border _pdfPreviewSelectorSwitch = null!;
    private RadioButton _pdfPreviewIconButton = null!;
    private RadioButton _pdfPreviewListButton = null!;
    private TextBlock _pdfPreviewIconLabel = null!;
    private TextBlock _pdfPreviewListLabel = null!;
    private TranslateTransform _pdfPreviewSelectionTransform = null!;
    private Transitions? _pdfPreviewSelectionTransitions;
    private bool _pdfPreviewDragActive;
    private bool _pdfPreviewDragMoved;
    private double _pdfPreviewDragStartPointerX;
    private double _pdfPreviewDragStartTransformX;
    private StackPanel _pdfToolOutputPdfPanel = null!;
    private StackPanel _pdfToolOutputFolderPanel = null!;
    private StackPanel _pdfPageSelectionPanel = null!;
    private StackPanel _pdfSimpleEditPanel = null!;
    private StackPanel _pdfTextOutputPanel = null!;
    private TextBlock _pdfToolOutputPdfLabel = null!;
    private TextBlock _pdfPageSelectionLabel = null!;
    private TextBlock _pdfPageSelectionHelpText = null!;
    private TextBox _pdfOutputFolderTextBox = null!;
    private TextBox _pdfOutputBaseNameTextBox = null!;
    private TextBox _pdfPageRangeTextBox = null!;
    private TextBox _imageOutputPdfTextBox = null!;
    private TextBox _imageOutputBaseNameTextBox = null!;
    private TextBox _pdfToolOutputPdfTextBox = null!;
    private TextBox _pdfToolOutputPdfBaseNameTextBox = null!;
    private TextBox _pdfToolOutputFolderTextBox = null!;
    private TextBox _pdfToolOutputBaseNameTextBox = null!;
    private TextBox _pdfPageSelectionTextBox = null!;
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
    private bool _themeTransitionActive;
    private bool _reduceMotion;
    private readonly Bitmap _lightHeaderIcon;
    private readonly Bitmap _darkHeaderIcon;

    public MainWindow()
        : this(Application.Current?.ActualThemeVariant == ThemeVariant.Dark)
    {
    }

    public MainWindow(bool isInitialDarkTheme)
    {
        InitializeComponent();
        _lightHeaderIcon = LoadAssetBitmap("avares://OfflinePDFConverter/Assets/AppIconLight.png");
        _darkHeaderIcon = LoadAssetBitmap("avares://OfflinePDFConverter/Assets/AppIconDark.png");
        _reduceMotion = ShouldReduceMotion();
        BindControls();
        ApplyTheme(isInitialDarkTheme);
        SetPdfDpiIndex(0);

        _pdfFilesList.ItemsSource = _pdfFiles;
        _pdfToolFilesList.ItemsSource = _pdfFiles;
        _pdfFiles.CollectionChanged += (_, _) => UpdatePdfFileEmptyHints();
        UpdatePdfFileEmptyHints();
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
        _themeTransitionRoot = Required<Grid>("ThemeTransitionRoot");
        _modeSelectorSwitch = Required<Border>("ModeSelectorSwitch");
        _themeToggleSwitch = Required<Border>("ThemeToggleSwitch");
        _themeToggleThumb = Required<Border>("ThemeToggleThumb");
        _themeLightLabel = Required<TextBlock>("ThemeLightLabel");
        _themeDarkLabel = Required<TextBlock>("ThemeDarkLabel");
        _themeToggleTransform = _themeToggleThumb.RenderTransform as TranslateTransform
            ?? throw new InvalidOperationException("テーマ切替インジケーターの移動設定が見つかりません。");
        _pdfModeButton = Required<RadioButton>("PdfModeButton");
        _pdfToolsModeButton = Required<RadioButton>("PdfToolsModeButton");
        _pdfModeLabel = Required<TextBlock>("PdfModeLabel");
        _pdfToolsModeLabel = Required<TextBlock>("PdfToolsModeLabel");
        _modeSelectionTransform = Required<Border>("ModeSelectionPill").RenderTransform as TranslateTransform
            ?? throw new InvalidOperationException("モード選択インジケーターの移動設定が見つかりません。");
        _modeSelectionTransitions = _modeSelectionTransform.Transitions;
        _themeToggleTransitions = _themeToggleTransform.Transitions;
        _directionSelectorHost = Required<Grid>("DirectionSelectorHost");
        _directionSelectorSwitch = Required<Border>("DirectionSelectorSwitch");
        _pdfToImageDirectionButton = Required<RadioButton>("PdfToImageDirectionButton");
        _imageToPdfDirectionButton = Required<RadioButton>("ImageToPdfDirectionButton");
        _pdfToImageDirectionLabel = Required<TextBlock>("PdfToImageDirectionLabel");
        _imageToPdfDirectionLabel = Required<TextBlock>("ImageToPdfDirectionLabel");
        _directionSelectionTransform = Required<Border>("DirectionSelectionPill").RenderTransform as TranslateTransform
            ?? throw new InvalidOperationException("変換方向インジケーターの移動設定が見つかりません。");
        _directionSelectionTransitions = _directionSelectionTransform.Transitions;
        _pdfPanel = Required<Grid>("PdfPanel");
        _imagePanel = Required<Grid>("ImagePanel");
        _pdfToolsPanel = Required<Grid>("PdfToolsPanel");
        _pdfFilesList = Required<ListBox>("PdfFilesList");
        _pdfToolFilesList = Required<ListBox>("PdfToolFilesList");
        _pdfFilesEmptyHint = Required<TextBlock>("PdfFilesEmptyHint");
        _pdfToolFilesEmptyHint = Required<TextBlock>("PdfToolFilesEmptyHint");
        _pdfPagePreviewThumbnailScroll = Required<ScrollViewer>("PdfPagePreviewThumbnailScroll");
        _pdfPagePreviewListScroll = Required<ScrollViewer>("PdfPagePreviewListScroll");
        _pdfPagePreviewThumbnailItems = Required<ItemsControl>("PdfPagePreviewThumbnailItems");
        _pdfPagePreviewListItems = Required<ItemsControl>("PdfPagePreviewListItems");
        _imageFilesList = Required<ListBox>("ImageFilesList");
        _pdfFormatCombo = Required<ComboBox>("PdfFormatCombo");
        _pdfDpiSliderSwitch = Required<Border>("PdfDpiSliderSwitch");
        _pdfDpiRange = Required<Border>("PdfDpiRange");
        _pdfDpiThumb = Required<Border>("PdfDpiThumb");
        _pdfDpiThumbTransform = _pdfDpiThumb.RenderTransform as TranslateTransform
            ?? throw new InvalidOperationException("解像度スライダーのつまみ移動設定が見つかりません。");
        _pdfDpiThumbTransition = _pdfDpiThumbTransform.Transitions?.OfType<DoubleTransition>().FirstOrDefault()
            ?? throw new InvalidOperationException("解像度スライダーのつまみアニメーション設定が見つかりません。");
        _pdfDpiRangeTransition = _pdfDpiRange.Transitions?.OfType<DoubleTransition>().FirstOrDefault()
            ?? throw new InvalidOperationException("解像度スライダーのトラックアニメーション設定が見つかりません。");
        _pdfDpiCurrentHint = Required<TextBlock>("PdfDpiCurrentHint");
        _pdfDpiTicks = new[]
        {
            Required<Border>("PdfDpiTickNormal"),
            Required<Border>("PdfDpiTickHigh"),
            Required<Border>("PdfDpiTickUltra")
        };
        _imagePageModeCombo = Required<ComboBox>("ImagePageModeCombo");
        _imageMarginCheckBox = Required<CheckBox>("ImageMarginCheckBox");
        _pdfToolOperationCombo = Required<ComboBox>("PdfToolOperationCombo");
        _pdfPreviewSelectorSwitch = Required<Border>("PdfPreviewSelectorSwitch");
        _pdfPreviewIconButton = Required<RadioButton>("PdfPreviewIconButton");
        _pdfPreviewListButton = Required<RadioButton>("PdfPreviewListButton");
        _pdfPreviewIconLabel = Required<TextBlock>("PdfPreviewIconLabel");
        _pdfPreviewListLabel = Required<TextBlock>("PdfPreviewListLabel");
        _pdfPreviewSelectionTransform = Required<Border>("PdfPreviewSelectionPill").RenderTransform as TranslateTransform
            ?? throw new InvalidOperationException("ページプレビュー表示インジケーターの移動設定が見つかりません。");
        _pdfPreviewSelectionTransitions = _pdfPreviewSelectionTransform.Transitions;
        _pdfToolOutputPdfPanel = Required<StackPanel>("PdfToolOutputPdfPanel");
        _pdfToolOutputFolderPanel = Required<StackPanel>("PdfToolOutputFolderPanel");
        _pdfPageSelectionPanel = Required<StackPanel>("PdfPageSelectionPanel");
        _pdfSimpleEditPanel = Required<StackPanel>("PdfSimpleEditPanel");
        _pdfTextOutputPanel = Required<StackPanel>("PdfTextOutputPanel");
        _pdfToolOutputPdfLabel = Required<TextBlock>("PdfToolOutputPdfLabel");
        _pdfPageSelectionLabel = Required<TextBlock>("PdfPageSelectionLabel");
        _pdfPageSelectionHelpText = Required<TextBlock>("PdfPageSelectionHelpText");
        _pdfOutputFolderTextBox = Required<TextBox>("PdfOutputFolderTextBox");
        _pdfOutputBaseNameTextBox = Required<TextBox>("PdfOutputBaseNameTextBox");
        _pdfPageRangeTextBox = Required<TextBox>("PdfPageRangeTextBox");
        _imageOutputPdfTextBox = Required<TextBox>("ImageOutputPdfTextBox");
        _imageOutputBaseNameTextBox = Required<TextBox>("ImageOutputBaseNameTextBox");
        _pdfToolOutputPdfTextBox = Required<TextBox>("PdfToolOutputPdfTextBox");
        _pdfToolOutputPdfBaseNameTextBox = Required<TextBox>("PdfToolOutputPdfBaseNameTextBox");
        _pdfToolOutputFolderTextBox = Required<TextBox>("PdfToolOutputFolderTextBox");
        _pdfToolOutputBaseNameTextBox = Required<TextBox>("PdfToolOutputBaseNameTextBox");
        _pdfPageSelectionTextBox = Required<TextBox>("PdfPageSelectionTextBox");
        _pdfPreviewHelpText = Required<TextBlock>("PdfPreviewHelpText");
        _startPdfButton = Required<Button>("StartPdfButton");
        _startImageButton = Required<Button>("StartImageButton");
        _startPdfToolButton = Required<Button>("StartPdfToolButton");
        _mainProgressBar = Required<ProgressBar>("MainProgressBar");
        _statusText = Required<TextBlock>("StatusText");

        _modeSelectorSwitch.AddHandler(
            PointerPressedEvent,
            OnModeSelectorPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _modeSelectorSwitch.PointerMoved += OnModeSelectorPointerMoved;
        _modeSelectorSwitch.PointerReleased += OnModeSelectorPointerReleased;
        _modeSelectorSwitch.PointerCaptureLost += OnModeSelectorPointerCaptureLost;

        _themeToggleSwitch.AddHandler(
            PointerPressedEvent,
            OnThemeSwitchPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _themeToggleSwitch.PointerMoved += OnThemeSwitchPointerMoved;
        _themeToggleSwitch.PointerReleased += OnThemeSwitchPointerReleased;
        _themeToggleSwitch.PointerCaptureLost += OnThemeSwitchPointerCaptureLost;

        _directionSelectorSwitch.AddHandler(
            PointerPressedEvent,
            OnDirectionSelectorPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _directionSelectorSwitch.PointerMoved += OnDirectionSelectorPointerMoved;
        _directionSelectorSwitch.PointerReleased += OnDirectionSelectorPointerReleased;
        _directionSelectorSwitch.PointerCaptureLost += OnDirectionSelectorPointerCaptureLost;

        _pdfDpiSliderSwitch.AddHandler(
            PointerPressedEvent,
            OnPdfDpiSliderPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _pdfDpiSliderSwitch.AddHandler(
            KeyDownEvent,
            OnPdfDpiSliderKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _pdfDpiSliderSwitch.PointerMoved += OnPdfDpiSliderPointerMoved;
        _pdfDpiSliderSwitch.PointerReleased += OnPdfDpiSliderPointerReleased;
        _pdfDpiSliderSwitch.PointerCaptureLost += OnPdfDpiSliderPointerCaptureLost;

        _pdfPreviewSelectorSwitch.AddHandler(
            PointerPressedEvent,
            OnPdfPreviewSelectorPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _pdfPreviewSelectorSwitch.AddHandler(
            KeyDownEvent,
            OnPdfPreviewSelectorKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _pdfPreviewSelectorSwitch.PointerMoved += OnPdfPreviewSelectorPointerMoved;
        _pdfPreviewSelectorSwitch.PointerReleased += OnPdfPreviewSelectorPointerReleased;
        _pdfPreviewSelectorSwitch.PointerCaptureLost += OnPdfPreviewSelectorPointerCaptureLost;

        if (_reduceMotion)
        {
            foreach (var animatable in this.GetVisualDescendants().OfType<Animatable>())
            {
                animatable.Transitions = null;
            }

            _modeSelectionTransform.Transitions = null;
            _pdfModeLabel.Transitions = null;
            _pdfToolsModeLabel.Transitions = null;
            _themeToggleTransform.Transitions = null;
            _themeToggleSwitch.Transitions = null;
            _themeToggleThumb.Transitions = null;
            _themeLightLabel.Transitions = null;
            _themeDarkLabel.Transitions = null;
            _directionSelectionTransform.Transitions = null;
            _pdfToImageDirectionLabel.Transitions = null;
            _imageToPdfDirectionLabel.Transitions = null;
            _pdfDpiThumbTransform.Transitions = null;
            _pdfDpiRange.Transitions = null;
            _pdfDpiCurrentHint.Transitions = null;
            foreach (var tick in _pdfDpiTicks)
            {
                tick.Transitions = null;
            }
            _pdfPreviewSelectionTransform.Transitions = null;
            _pdfPreviewIconLabel.Transitions = null;
            _pdfPreviewListLabel.Transitions = null;
            _modeSelectionTransitions = null;
            _directionSelectionTransitions = null;
            _pdfPreviewSelectionTransitions = null;
            _themeToggleTransitions = null;
        }
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

    private void OnPdfDpiSliderKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
            case Key.Up:
                SetPdfDpiIndex(_pdfDpiIndex - 1);
                e.Handled = true;
                break;
            case Key.Right:
            case Key.Down:
                SetPdfDpiIndex(_pdfDpiIndex + 1);
                e.Handled = true;
                break;
            case Key.Home:
                SetPdfDpiIndex(0);
                e.Handled = true;
                break;
            case Key.End:
                SetPdfDpiIndex(PdfDpiLabels.Length - 1);
                e.Handled = true;
                break;
        }
    }

    private void OnPdfDpiSliderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_pdfDpiSliderSwitch).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pdfDpiDragActive = true;
        _pdfDpiThumbTransition.Duration = TimeSpan.FromMilliseconds(150);
        _pdfDpiRangeTransition.Duration = TimeSpan.FromMilliseconds(150);
        UpdatePdfDpiDragVisual(e.GetPosition(_pdfDpiSliderSwitch).X);
        e.Pointer.Capture(_pdfDpiSliderSwitch);
        e.Handled = true;
    }

    private void OnPdfDpiSliderPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_pdfDpiDragActive)
        {
            return;
        }

        var pointerX = e.GetPosition(_pdfDpiSliderSwitch).X;
        UpdatePdfDpiDragVisual(pointerX);
        e.Handled = true;
    }

    private void OnPdfDpiSliderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pdfDpiDragActive)
        {
            return;
        }

        UpdatePdfDpiDragVisual(e.GetPosition(_pdfDpiSliderSwitch).X);
        FinishPdfDpiDrag();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnPdfDpiSliderPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_pdfDpiDragActive)
        {
            FinishPdfDpiDrag();
        }
    }

    private void UpdatePdfDpiDragVisual(double pointerX)
    {
        var centeredX = Math.Clamp(pointerX - 8, PdfDpiTrackInset, PdfDpiTrackInset + PdfDpiDragMaximum);
        _pdfDpiDragVisualX = centeredX - PdfDpiTrackInset;
        _pdfDpiThumbTransform.X = _pdfDpiDragVisualX;
        _pdfDpiRange.Width = centeredX;
        var previewIndex = (int)Math.Round(_pdfDpiDragVisualX / (PdfDpiDragMaximum / 2));
        _pdfDpiCurrentHint.Text = $"{PdfDpiLabels[previewIndex]} · {PdfDpiValues[previewIndex]} dpi";
        SetPdfDpiTickClasses(previewIndex);
    }

    private void FinishPdfDpiDrag()
    {
        var targetIndex = (int)Math.Round(_pdfDpiDragVisualX / (PdfDpiDragMaximum / 2));
        _pdfDpiDragActive = false;
        _pdfDpiThumbTransition.Duration = TimeSpan.FromMilliseconds(300);
        _pdfDpiRangeTransition.Duration = TimeSpan.FromMilliseconds(300);
        SetPdfDpiIndex(targetIndex);
        _pdfDpiSliderSwitch.Focus();
    }

    private void SetPdfDpiIndex(int index)
    {
        index = Math.Clamp(index, 0, PdfDpiLabels.Length - 1);
        _pdfDpiIndex = index;
        var label = PdfDpiLabels[index];
        var dpi = PdfDpiValues[index];

        _pdfDpiThumbTransform.X = PdfDpiThumbPositions[index];
        _pdfDpiRange.Width = PdfDpiRangeWidths[index];
        _pdfDpiCurrentHint.Text = $"{label} · {dpi} dpi";
        SetPdfDpiTickClasses(index);
        AutomationProperties.SetHelpText(
            _pdfDpiSliderSwitch,
            $"現在は{label}、{dpi}dpiです。左右キーまたはドラッグで変更します");
    }

    private void SetPdfDpiTickClasses(int index)
    {
        for (var i = 0; i < _pdfDpiTicks.Length; i++)
        {
            _pdfDpiTicks[i].Classes.Set("selected", i <= index);
        }
    }

    private void OnPdfToolsModeClick(object? sender, RoutedEventArgs e)
    {
        SetMode(ConversionMode.PdfTools);
    }

    private void OnModeSelectorKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
            case Key.Up:
            case Key.Home:
                SetMode(ConversionMode.PdfTools);
                _pdfToolsModeButton.Focus();
                e.Handled = true;
                break;
            case Key.Right:
            case Key.Down:
            case Key.End:
                SetMode(ConversionMode.PdfToImage);
                _pdfModeButton.Focus();
                e.Handled = true;
                break;
        }
    }

    private void OnModeSelectorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_modeSelectorSwitch).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _modeDragActive = true;
        _modeDragMoved = false;
        _modeDragStartPointerX = e.GetPosition(_modeSelectorSwitch).X;
        _modeDragStartTransformX = _modeSelectionTransform.X;
        _modeSelectionTransform.Transitions = null;
        e.Pointer.Capture(_modeSelectorSwitch);
        e.Handled = true;
    }

    private void OnModeSelectorPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_modeDragActive)
        {
            return;
        }

        var delta = e.GetPosition(_modeSelectorSwitch).X - _modeDragStartPointerX;
        _modeDragMoved |= Math.Abs(delta) >= DragActivationDistance;
        _modeSelectionTransform.X = Math.Clamp(
            _modeDragStartTransformX + delta,
            0,
            ModeDragMaximum);
        e.Handled = true;
    }

    private void OnModeSelectorPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_modeDragActive)
        {
            return;
        }

        var releaseX = e.GetPosition(_modeSelectorSwitch).X;
        var targetPdfTools = _modeDragMoved
            ? _modeSelectionTransform.X < ModeDragMaximum / 2
            : releaseX < _modeSelectorSwitch.Bounds.Width / 2;
        FinishModeDrag(targetPdfTools);
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnModeSelectorPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_modeDragActive)
        {
            FinishModeDrag(_modeSelectionTransform.X < ModeDragMaximum / 2);
        }
    }

    private void FinishModeDrag(bool selectPdfTools)
    {
        _modeDragActive = false;
        _modeSelectionTransform.Transitions = _modeSelectionTransitions;
        SetMode(selectPdfTools ? ConversionMode.PdfTools : ConversionMode.PdfToImage);
        (selectPdfTools ? _pdfToolsModeButton : _pdfModeButton).Focus();
    }

    private void OnDirectionSelectorKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
            case Key.Up:
            case Key.Home:
                SetMode(ConversionMode.PdfToImage);
                _pdfToImageDirectionButton.Focus();
                e.Handled = true;
                break;
            case Key.Right:
            case Key.Down:
            case Key.End:
                SetMode(ConversionMode.ImageToPdf);
                _imageToPdfDirectionButton.Focus();
                e.Handled = true;
                break;
        }
    }

    private void OnDirectionSelectorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_directionSelectorSwitch).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _directionDragActive = true;
        _directionDragMoved = false;
        _directionDragStartPointerX = e.GetPosition(_directionSelectorSwitch).X;
        _directionDragStartTransformX = _directionSelectionTransform.X;
        _directionSelectionTransform.Transitions = null;
        e.Pointer.Capture(_directionSelectorSwitch);
        e.Handled = true;
    }

    private void OnDirectionSelectorPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_directionDragActive)
        {
            return;
        }

        var delta = e.GetPosition(_directionSelectorSwitch).X - _directionDragStartPointerX;
        _directionDragMoved |= Math.Abs(delta) >= DragActivationDistance;
        _directionSelectionTransform.X = Math.Clamp(
            _directionDragStartTransformX + delta,
            0,
            DirectionDragMaximum);
        e.Handled = true;
    }

    private void OnDirectionSelectorPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_directionDragActive)
        {
            return;
        }

        var releaseX = e.GetPosition(_directionSelectorSwitch).X;
        var targetPdfToImage = _directionDragMoved
            ? _directionSelectionTransform.X < DirectionDragMaximum / 2
            : releaseX < _directionSelectorSwitch.Bounds.Width / 2;
        FinishDirectionDrag(targetPdfToImage);
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnDirectionSelectorPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_directionDragActive)
        {
            FinishDirectionDrag(_directionSelectionTransform.X < DirectionDragMaximum / 2);
        }
    }

    private void FinishDirectionDrag(bool selectPdfToImage)
    {
        _directionDragActive = false;
        _directionSelectionTransform.Transitions = _directionSelectionTransitions;
        SetMode(selectPdfToImage ? ConversionMode.PdfToImage : ConversionMode.ImageToPdf);
        (selectPdfToImage ? _pdfToImageDirectionButton : _imageToPdfDirectionButton).Focus();
    }

    private void OnThemeSwitchPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_themeToggleSwitch).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _themeDragActive = true;
        _themeDragMoved = false;
        _themeDragStartPointerX = e.GetPosition(_themeToggleSwitch).X;
        _themeDragStartTransformX = _themeToggleTransform.X;
        _themeToggleTransform.Transitions = null;
        _themeToggleSwitch.Focus();
        e.Pointer.Capture(_themeToggleSwitch);
        e.Handled = true;
    }

    private void OnThemeSwitchPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_themeDragActive)
        {
            return;
        }

        var delta = e.GetPosition(_themeToggleSwitch).X - _themeDragStartPointerX;
        _themeDragMoved |= Math.Abs(delta) >= DragActivationDistance;
        _themeToggleTransform.X = Math.Clamp(
            _themeDragStartTransformX + delta,
            0,
            ThemeDragMaximum);
        e.Handled = true;
    }

    private void OnThemeSwitchPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_themeDragActive)
        {
            return;
        }

        var releaseX = e.GetPosition(_themeToggleSwitch).X;
        var targetDarkTheme = _themeDragMoved
            ? _themeToggleTransform.X >= ThemeDragMaximum / 2
            : releaseX >= _themeToggleSwitch.Bounds.Width / 2;
        FinishThemeDrag(targetDarkTheme);
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnThemeSwitchPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_themeDragActive)
        {
            FinishThemeDrag(_themeToggleTransform.X >= ThemeDragMaximum / 2);
        }
    }

    private void FinishThemeDrag(bool useDarkTheme)
    {
        _themeDragActive = false;
        _themeToggleTransform.Transitions = _themeToggleTransitions;
        if (_reduceMotion)
        {
            ApplyTheme(useDarkTheme);
            return;
        }

        _ = ApplyThemeWithUnifiedFadeAsync(useDarkTheme);
    }

    private void OnThemeSelectorKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
            case Key.Up:
            case Key.Home:
                FinishThemeDrag(useDarkTheme: false);
                e.Handled = true;
                break;
            case Key.Right:
            case Key.Down:
            case Key.End:
                FinishThemeDrag(useDarkTheme: true);
                e.Handled = true;
                break;
            case Key.Space:
            case Key.Enter:
                FinishThemeDrag(!_isDarkTheme);
                e.Handled = true;
                break;
        }
    }

    private async Task ApplyThemeWithUnifiedFadeAsync(bool isDarkTheme)
    {
        if (_themeTransitionActive)
        {
            return;
        }

        _themeTransitionActive = true;
        var restoreThemeSwitchEnabled = _themeToggleSwitch.IsEnabled;
        _themeToggleSwitch.IsEnabled = false;
        _isDarkTheme = isDarkTheme;
        _themeToggleTransform.X = isDarkTheme ? ThemeDragMaximum : 0;

        try
        {
            var animatedBrushes = ThemePalette
                .Select(entry =>
                {
                    var brush = Resources[entry.Key] as SolidColorBrush;
                    var target = Color.Parse(isDarkTheme ? entry.Dark : entry.Light);
                    return (Brush: brush, Start: brush?.Color ?? target, Target: target);
                })
                .Where(entry => entry.Brush is not null)
                .ToArray();

            var themeChromeApplied = false;
            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                var linearProgress = Math.Clamp(
                    stopwatch.Elapsed.TotalMilliseconds / ThemePaletteDurationMilliseconds,
                    0,
                    1);
                var easedProgress = linearProgress * linearProgress * (3 - (2 * linearProgress));

                if (!themeChromeApplied && linearProgress >= 0.5)
                {
                    ApplyThemeChrome(isDarkTheme);
                    themeChromeApplied = true;
                }

                foreach (var entry in animatedBrushes)
                {
                    entry.Brush!.Color = InterpolateColor(entry.Start, entry.Target, easedProgress);
                }

                if (linearProgress >= 1)
                {
                    break;
                }

                await Task.Delay(16);
            }

            if (!themeChromeApplied)
            {
                ApplyThemeChrome(isDarkTheme);
            }
        }
        finally
        {
            _themeToggleSwitch.IsEnabled = restoreThemeSwitchEnabled;
            _themeTransitionActive = false;
        }
    }

    private void ApplyTheme(bool isDarkTheme)
    {
        _isDarkTheme = isDarkTheme;
        ApplyThemeChrome(isDarkTheme);

        foreach (var entry in ThemePalette)
        {
            var color = Color.Parse(isDarkTheme ? entry.Dark : entry.Light);
            if (Resources[entry.Key] is SolidColorBrush brush)
            {
                brush.Color = color;
            }
            else
            {
                Resources[entry.Key] = new SolidColorBrush(color);
            }
        }
    }

    private void ApplyThemeChrome(bool isDarkTheme)
    {
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = isDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        _themeToggleTransform.X = isDarkTheme ? ThemeDragMaximum : 0;
        _themeLightLabel.Classes.Set("selected", !isDarkTheme);
        _themeDarkLabel.Classes.Set("selected", isDarkTheme);
        _appHeaderIcon.Source = isDarkTheme ? _darkHeaderIcon : _lightHeaderIcon;
    }

    private static Color InterpolateColor(Color start, Color target, double progress)
    {
        static byte Mix(byte from, byte to, double amount)
        {
            return (byte)Math.Clamp(Math.Round(from + ((to - from) * amount)), 0, 255);
        }

        return Color.FromArgb(
            Mix(start.A, target.A, progress),
            Mix(start.R, target.R, progress),
            Mix(start.G, target.G, progress),
            Mix(start.B, target.B, progress));
    }

    private static Bitmap LoadAssetBitmap(string uri)
    {
        return new Bitmap(AssetLoader.Open(new Uri(uri)));
    }

    private void SetMode(ConversionMode mode)
    {
        _mode = mode;
        _pdfPanel.IsVisible = mode == ConversionMode.PdfToImage;
        _imagePanel.IsVisible = mode == ConversionMode.ImageToPdf;
        _pdfToolsPanel.IsVisible = mode == ConversionMode.PdfTools;
        _directionSelectorHost.IsVisible = mode != ConversionMode.PdfTools;
        _startPdfButton.IsVisible = mode == ConversionMode.PdfToImage;
        _startImageButton.IsVisible = mode == ConversionMode.ImageToPdf;
        _startPdfToolButton.IsVisible = mode == ConversionMode.PdfTools;
        var isPdfToolsMode = mode == ConversionMode.PdfTools;
        _pdfToolsModeButton.IsChecked = isPdfToolsMode;
        _pdfModeButton.IsChecked = !isPdfToolsMode;
        _modeSelectionTransform.X = isPdfToolsMode ? 0 : ModeDragMaximum;
        var isPdfToImage = mode != ConversionMode.ImageToPdf;
        _pdfToImageDirectionButton.IsChecked = isPdfToImage;
        _imageToPdfDirectionButton.IsChecked = !isPdfToImage;
        _directionSelectionTransform.X = isPdfToImage ? 0 : DirectionDragMaximum;
        SetStatus(null);

        if (mode == ConversionMode.PdfTools)
        {
            RefreshPdfToolPreview();
        }
    }

    private void UpdatePdfFileEmptyHints()
    {
        var showHint = _pdfFiles.Count == 0;
        _pdfFilesEmptyHint.IsVisible = showHint;
        _pdfToolFilesEmptyHint.IsVisible = showHint;
    }

    private static bool ShouldReduceMotion()
    {
        var environmentOverride = Environment.GetEnvironmentVariable("OFFLINE_PDF_CONVERTER_REDUCE_MOTION");
        if (!string.IsNullOrWhiteSpace(environmentOverride))
        {
            return environmentOverride.Equals("1", StringComparison.OrdinalIgnoreCase)
                || environmentOverride.Equals("true", StringComparison.OrdinalIgnoreCase)
                || environmentOverride.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        if (OperatingSystem.IsWindows())
        {
            return !SystemParametersInfo(
                SpiGetClientAreaAnimation,
                0,
                out var animationsEnabled,
                0)
                || !animationsEnabled;
        }

        if (OperatingSystem.IsMacOS())
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "/usr/bin/defaults",
                    ArgumentList =
                    {
                        "read",
                        "com.apple.universalaccess",
                        "reduceMotion"
                    },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process is null)
                {
                    return false;
                }

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(250);
                return process.ExitCode == 0 && output.Trim() == "1";
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private const uint SpiGetClientAreaAnimation = 0x1042;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint action,
        uint parameter,
        [MarshalAs(UnmanagedType.Bool)] out bool value,
        uint update);

    private async void OnAddPdfFilesClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "PDFファイルを選択",
            AllowMultiple = true,
            FileTypeFilter = new[] { PdfFileType }
        });

        await AddPdfPathsAsync(files.Select(file => file.TryGetLocalPath()).WhereNotNull());
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
        var suggestedBaseName = FileNameHelper.BuildOutputBaseName(
            _imageFiles.Select(item => item.Path),
            "converted_images");
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "出力PDFを保存",
            SuggestedFileName = $"{suggestedBaseName}.pdf",
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
        var (suggestedName, title) = operation switch
        {
            PdfToolOperation.DeletePages => ("deleted_pages.pdf", "ページ削除後のPDFを保存"),
            PdfToolOperation.ExtractPages => ("extracted_pages.pdf", "選択ページのPDFを保存"),
            PdfToolOperation.SimpleEdit => ("edited.pdf", "編集後のPDFを保存"),
            PdfToolOperation.ExtractText => ("extracted_text.txt", "テキストを保存"),
            _ => ("merged.pdf", "結合後のPDFを保存")
        };

        var isTextOutput = operation == PdfToolOperation.ExtractText;
        var suggestedBaseName = FileNameHelper.BuildOutputBaseName(
            _pdfFiles.Select(item => item.Path),
            Path.GetFileNameWithoutExtension(suggestedName));
        suggestedName = $"{suggestedBaseName}{(isTextOutput ? ".txt" : ".pdf")}";

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = isTextOutput ? "txt" : "pdf",
            FileTypeChoices = new[] { isTextOutput ? TextFileType : PdfFileType }
        });

        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            _pdfToolOutputPdfTextBox.Text = isTextOutput
                ? EnsureTextExtension(path)
                : EnsurePdfExtension(path);
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
        var selectedPaths = (_mode == ConversionMode.PdfTools ? _pdfToolFilesList : _pdfFilesList)
            .SelectedItems?
            .Cast<FileItem>()
            .Select(item => item.Path)
            .ToList() ?? new List<string>();
        RemoveSelected(_mode == ConversionMode.PdfTools ? _pdfToolFilesList : _pdfFilesList, _pdfFiles);
        foreach (var path in selectedPaths)
        {
            _pdfPasswords.Remove(path);
            foreach (var key in _pdfTextSelections.Keys
                         .Where(key => string.Equals(key.PdfPath, path, StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                _pdfTextSelections.Remove(key);
            }
        }

        RefreshPdfToolPreview();
    }

    private void OnRemoveImageFilesClick(object? sender, RoutedEventArgs e)
    {
        RemoveSelected(_imageFilesList, _imageFiles);
    }

    private void OnClearPdfFilesClick(object? sender, RoutedEventArgs e)
    {
        _pdfFiles.Clear();
        _pdfPasswords.Clear();
        _pdfTextEdits.Clear();
        _pdfShapeEdits.Clear();
        _pdfTextSelections.Clear();
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

    private void OnPdfPreviewIconClick(object? sender, RoutedEventArgs e)
    {
        SetPdfPreviewDisplay(useListView: false);
    }

    private void OnPdfPreviewListClick(object? sender, RoutedEventArgs e)
    {
        SetPdfPreviewDisplay(useListView: true);
    }

    private void OnPdfPreviewSelectorKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
            case Key.Up:
            case Key.Home:
                SetPdfPreviewDisplay(useListView: false);
                e.Handled = true;
                break;
            case Key.Right:
            case Key.Down:
            case Key.End:
                SetPdfPreviewDisplay(useListView: true);
                e.Handled = true;
                break;
        }
    }

    private void OnPdfPreviewSelectorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_pdfPreviewSelectorSwitch).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pdfPreviewDragActive = true;
        _pdfPreviewDragMoved = false;
        _pdfPreviewDragStartPointerX = e.GetPosition(_pdfPreviewSelectorSwitch).X;
        _pdfPreviewDragStartTransformX = _pdfPreviewSelectionTransform.X;
        _pdfPreviewSelectionTransform.Transitions = null;
        e.Pointer.Capture(_pdfPreviewSelectorSwitch);
        e.Handled = true;
    }

    private void OnPdfPreviewSelectorPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_pdfPreviewDragActive)
        {
            return;
        }

        var delta = e.GetPosition(_pdfPreviewSelectorSwitch).X - _pdfPreviewDragStartPointerX;
        _pdfPreviewDragMoved |= Math.Abs(delta) >= DragActivationDistance;
        _pdfPreviewSelectionTransform.X = Math.Clamp(
            _pdfPreviewDragStartTransformX + delta,
            0,
            PreviewViewDragMaximum);
        e.Handled = true;
    }

    private void OnPdfPreviewSelectorPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pdfPreviewDragActive)
        {
            return;
        }

        var releaseX = e.GetPosition(_pdfPreviewSelectorSwitch).X;
        var useListView = _pdfPreviewDragMoved
            ? _pdfPreviewSelectionTransform.X >= PreviewViewDragMaximum / 2
            : releaseX >= _pdfPreviewSelectorSwitch.Bounds.Width / 2;
        FinishPdfPreviewDrag(useListView);
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnPdfPreviewSelectorPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_pdfPreviewDragActive)
        {
            FinishPdfPreviewDrag(_pdfPreviewSelectionTransform.X >= PreviewViewDragMaximum / 2);
        }
    }

    private void FinishPdfPreviewDrag(bool useListView)
    {
        _pdfPreviewDragActive = false;
        _pdfPreviewSelectionTransform.Transitions = _pdfPreviewSelectionTransitions;
        SetPdfPreviewDisplay(useListView);
    }

    private void SetPdfPreviewDisplay(bool useListView)
    {
        _isPdfPreviewListView = useListView;
        UpdatePdfPreviewDisplay();
        (useListView ? _pdfPreviewListButton : _pdfPreviewIconButton).Focus();
    }

    private void OnPdfPreviewPageSelectionChanged(object? sender, RoutedEventArgs e)
    {
        var operation = GetPdfToolOperation();
        if (operation is not (PdfToolOperation.DeletePages or PdfToolOperation.ExtractPages or PdfToolOperation.ExtractText))
        {
            return;
        }

        if (sender is CheckBox { DataContext: PdfPagePreviewItem item } checkBox)
        {
            item.IsPageSelected = checkBox.IsChecked == true;
            if (operation == PdfToolOperation.ExtractText && item.IsPageSelected)
            {
                _pdfTextSelections.Remove((item.PdfPath, item.PageNumber));
                item.HasEditMarker = false;
            }
        }

        if (operation == PdfToolOperation.ExtractText)
        {
            return;
        }

        var pages = _pdfPagePreviews
            .Where(item => item.IsPageSelected)
            .Select(item => item.PageNumber)
            .Distinct()
            .Order()
            .ToList();

        _pdfPageSelectionTextBox.Text = FormatPageRanges(pages);
    }

    private void OnPdfPreviewImagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var operation = GetPdfToolOperation();
        if (sender is not Image image
            || image.DataContext is not PdfPagePreviewItem item)
        {
            return;
        }

        if (operation == PdfToolOperation.SimpleEdit)
        {
            _ = OpenSimpleEditWindowAsync(item);
        }
        else if (operation == PdfToolOperation.ExtractText)
        {
            _ = OpenTextSelectionWindowAsync(item);
        }
    }

    private void OnPdfPreviewListItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var operation = GetPdfToolOperation();
        if (sender is not Control control
            || control.DataContext is not PdfPagePreviewItem item)
        {
            return;
        }

        if (operation == PdfToolOperation.SimpleEdit)
        {
            _ = OpenSimpleEditWindowAsync(item);
        }
        else if (operation == PdfToolOperation.ExtractText)
        {
            _ = OpenTextSelectionWindowAsync(item);
        }
    }

    private async void OnStartPdfConversionClick(object? sender, RoutedEventArgs e)
    {
        var request = new PdfToImageRequest(
            _pdfFiles.Select(item => item.Path).ToList(),
            _pdfOutputFolderTextBox.Text?.Trim() ?? string.Empty,
            _pdfOutputBaseNameTextBox.Text?.Trim() ?? string.Empty,
            GetPdfImageFormat(),
            GetPdfDpi(),
            _pdfPageRangeTextBox.Text?.Trim() ?? string.Empty,
            GetPdfPasswords(),
            (int)(this.FindControl<NumericUpDown>("JpegQualityInput")?.Value ?? 95));

        await RunBatchAsync(request.PdfFiles,
            (file, progress, token) => _pdfToImageService.ConvertAsync(request with { PdfFiles = new[] { file } }, progress, token));
    }

    private async void OnStartImageConversionClick(object? sender, RoutedEventArgs e)
    {
        var imagePaths = _imageFiles.Select(item => item.Path).ToList();
        var outputPdfPath = BuildOutputPdfPath(
            _imageOutputPdfTextBox.Text?.Trim() ?? string.Empty,
            _imageOutputBaseNameTextBox.Text?.Trim() ?? string.Empty,
            imagePaths);
        if (!string.IsNullOrWhiteSpace(outputPdfPath)
            && !outputPdfPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            outputPdfPath = $"{outputPdfPath}.pdf";
            _imageOutputPdfTextBox.Text = outputPdfPath;
        }

        var request = new ImageToPdfRequest(
            imagePaths,
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
            case PdfToolOperation.ExtractPages:
                await StartExtractPdfPagesAsync();
                break;
            case PdfToolOperation.SimpleEdit:
                await StartSimpleEditPdfAsync();
                break;
            case PdfToolOperation.ExtractText:
                await StartExtractPdfTextAsync();
                break;
        }
    }

    private async Task StartMergePdfAsync()
    {
        var pdfPaths = _pdfFiles.Select(item => item.Path).ToList();
        var outputPdfPath = BuildOutputPdfPath(
            _pdfToolOutputPdfTextBox.Text?.Trim() ?? string.Empty,
            _pdfToolOutputPdfBaseNameTextBox.Text?.Trim() ?? string.Empty,
            pdfPaths);
        _pdfToolOutputPdfTextBox.Text = outputPdfPath;

        var request = new PdfMergeRequest(
            pdfPaths,
            outputPdfPath,
            GetPdfPasswords());

        await RunConversionAsync(
            (progress, token) => _pdfDocumentService.MergeAsync(request, progress, token),
            "PDFの結合が完了しました。");
    }

    private async Task StartSplitPdfAsync()
    {
        var request = new PdfSplitRequest(
            _pdfFiles.Select(item => item.Path).ToList(),
            _pdfToolOutputFolderTextBox.Text?.Trim() ?? string.Empty,
            _pdfToolOutputBaseNameTextBox.Text?.Trim() ?? string.Empty,
            GetPdfPasswords());

        await RunBatchAsync(request.PdfFiles,
            (file, progress, token) => _pdfDocumentService.SplitAsync(request with { PdfFiles = new[] { file } }, progress, token));
    }

    private async Task StartDeletePdfPagesAsync()
    {
        var pdfPaths = _pdfFiles.Select(item => item.Path).ToList();
        var outputPdfPath = BuildOutputPdfPath(
            _pdfToolOutputPdfTextBox.Text?.Trim() ?? string.Empty,
            _pdfToolOutputPdfBaseNameTextBox.Text?.Trim() ?? string.Empty,
            pdfPaths);
        _pdfToolOutputPdfTextBox.Text = outputPdfPath;

        var request = new PdfDeletePagesRequest(
            pdfPaths,
            _pdfPageSelectionTextBox.Text?.Trim() ?? string.Empty,
            outputPdfPath,
            GetPdfPasswords());

        await RunConversionAsync(
            (progress, token) => _pdfDocumentService.DeletePagesAsync(request, progress, token),
            "指定ページを削除したPDFを作成しました。");
    }

    private async Task StartExtractPdfPagesAsync()
    {
        var pdfPaths = _pdfFiles.Select(item => item.Path).ToList();
        var outputPdfPath = BuildOutputPdfPath(
            _pdfToolOutputPdfTextBox.Text?.Trim() ?? string.Empty,
            _pdfToolOutputPdfBaseNameTextBox.Text?.Trim() ?? string.Empty,
            pdfPaths);
        _pdfToolOutputPdfTextBox.Text = outputPdfPath;

        var request = new PdfExtractPagesRequest(
            pdfPaths,
            _pdfPageSelectionTextBox.Text?.Trim() ?? string.Empty,
            outputPdfPath,
            GetPdfPasswords());

        await RunConversionAsync(
            (progress, token) => _pdfDocumentService.ExtractPagesAsync(request, progress, token),
            "選択ページを1つのPDFとして出力しました。");
    }

    private async Task StartSimpleEditPdfAsync()
    {
        var pdfPaths = _pdfFiles.Select(item => item.Path).ToList();
        var outputPdfPath = BuildOutputPdfPath(
            _pdfToolOutputPdfTextBox.Text?.Trim() ?? string.Empty,
            _pdfToolOutputPdfBaseNameTextBox.Text?.Trim() ?? string.Empty,
            pdfPaths);
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
                edit.TextAlignment,
                edit.IsBold,
                edit.IsUnderline))
            .ToList();
        var shapes = _pdfShapeEdits
            .Select(shape => new PdfShapeEditItem(
                shape.PageNumber,
                shape.X,
                shape.Y,
                shape.Width,
                shape.Height,
                shape.ShapeType,
                shape.FillColorHex,
                shape.StrokeColorHex,
                shape.StrokeThickness,
                shape.CornerRadius,
                shape.RotationDegrees))
            .ToList();

        var request = new PdfSimpleEditRequest(
            pdfPaths,
            edits,
            shapes,
            outputPdfPath,
            GetPdfPasswords());

        await RunConversionAsync(
            (progress, token) => _pdfDocumentService.SimpleEditAsync(request, progress, token),
            "文字・テキストや図形を追加したPDFを作成しました。");
    }

    private async Task StartExtractPdfTextAsync()
    {
        var pdfPaths = _pdfFiles.Select(item => item.Path).ToList();
        var outputTextPath = BuildOutputTextPath(
            _pdfToolOutputPdfTextBox.Text?.Trim() ?? string.Empty,
            _pdfToolOutputPdfBaseNameTextBox.Text?.Trim() ?? string.Empty,
            pdfPaths);
        _pdfToolOutputPdfTextBox.Text = outputTextPath;

        var request = new PdfTextExtractionRequest(
            pdfPaths,
            outputTextPath,
            GetPdfPasswords(),
            _pdfPagePreviews
                .Where(item => item.IsPageSelected)
                .GroupBy(item => item.PdfPath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<int>)group.Select(item => item.PageNumber).Distinct().Order().ToList(),
                    StringComparer.OrdinalIgnoreCase),
            _pdfTextSelections
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Value.Text))
                .Select(entry => new PdfTextSelectionItem(
                    entry.Key.PdfPath,
                    entry.Key.PageNumber,
                    entry.Value.Text))
                .ToList());

        await RunConversionAsync(
            (progress, token) => _pdfTextExtractionService.ExtractAsync(request, progress, token),
            "テキスト出力が完了しました。");
    }

    private async Task OpenTextSelectionWindowAsync(PdfPagePreviewItem pageItem)
    {
        try
        {
            var password = _pdfPasswords.TryGetValue(pageItem.PdfPath, out var value)
                ? value
                : string.Empty;
            var words = await Task.Run(() => ExtractSelectableWords(pageItem, password));
            var key = (pageItem.PdfPath, pageItem.PageNumber);
            var selectedIndices = _pdfTextSelections.TryGetValue(key, out var current)
                ? current.SelectedWordIndices
                : Array.Empty<int>();
            var window = new PdfTextSelectionWindow(
                $"{Path.GetFileName(pageItem.PdfPath)} — {pageItem.PageNumber}ページ",
                pageItem.Thumbnail,
                pageItem.PageWidthPoints,
                pageItem.PageHeightPoints,
                words,
                selectedIndices);
            var result = await window.ShowDialog<PdfTextSelectionResult?>(this);
            if (result == null)
            {
                return;
            }

            if (result.SelectedWordIndices.Count == 0 || string.IsNullOrWhiteSpace(result.Text))
            {
                _pdfTextSelections.Remove(key);
                pageItem.HasEditMarker = false;
                SetStatus($"{pageItem.PageNumber}ページの個別テキスト選択を解除しました。");
                return;
            }

            _pdfTextSelections[key] = new PdfTextSelectionState(result.SelectedWordIndices, result.Text);
            pageItem.IsPageSelected = false;
            pageItem.HasEditMarker = true;
            pageItem.EditMarkerLeft = 108;
            pageItem.EditMarkerTop = 6;
            SetStatus($"{pageItem.PageNumber}ページで{result.SelectedWordIndices.Count}文字を選択しました。");
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("テキストを選択できません", FriendlyErrorFormatter.ToUserMessage(ex));
        }
    }

    private static IReadOnlyList<PdfSelectableWord> ExtractSelectableWords(
        PdfPagePreviewItem pageItem,
        string password)
    {
        using var document = string.IsNullOrEmpty(password)
            ? PdfDocument.Open(pageItem.PdfPath)
            : PdfDocument.Open(pageItem.PdfPath, new ParsingOptions { Password = password });
        var page = document.GetPage(pageItem.PageNumber);
        var scaleX = pageItem.PageWidthPoints / page.Width;
        var scaleY = pageItem.PageHeightPoints / page.Height;
        var characters = new List<PdfSelectableWord>();
        var characterIndex = 0;
        var wordIndex = 0;
        foreach (var word in page.GetWords())
        {
            foreach (var letter in word.Letters)
            {
                var elementStarts = StringInfo.ParseCombiningCharacters(letter.Value);
                var textElements = elementStarts
                    .Select((start, index) => letter.Value.Substring(
                        start,
                        (index + 1 < elementStarts.Length ? elementStarts[index + 1] : letter.Value.Length) - start))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToList();
                if (textElements.Count == 0)
                {
                    continue;
                }

                var bounds = letter.BoundingBox;
                var elementWidth = bounds.Width / textElements.Count;
                for (var elementIndex = 0; elementIndex < textElements.Count; elementIndex++)
                {
                    characters.Add(new PdfSelectableWord(
                        characterIndex++,
                        wordIndex,
                        textElements[elementIndex],
                        (bounds.Left + (elementWidth * elementIndex)) * scaleX,
                        (page.Height - bounds.Top) * scaleY,
                        elementWidth * scaleX,
                        bounds.Height * scaleY));
                }
            }

            wordIndex++;
        }

        return characters;
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

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Formats.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var dropped = e.DataTransfer.TryGetFiles();
        if (dropped == null)
        {
            return;
        }

        var paths = ExpandDroppedPaths(dropped.Select(file => file.TryGetLocalPath()).WhereNotNull());

        if (_mode == ConversionMode.PdfToImage)
        {
            await AddPdfPathsAsync(paths);
        }
        else if (_mode == ConversionMode.PdfTools)
        {
            await AddPdfPathsAsync(paths);
        }
        else
        {
            AddImagePaths(paths);
        }
    }

    private async Task AddPdfPathsAsync(IEnumerable<string> paths)
    {
        var existing = _pdfFiles
            .Select(item => item.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths
                     .Where(IsPdfFile)
                     .Where(File.Exists)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!existing.Add(path))
            {
                continue;
            }

            if (RequiresPdfPassword(path))
            {
                var password = await PromptForPdfPasswordAsync(path);
                if (password == null)
                {
                    existing.Remove(path);
                    continue;
                }

                _pdfPasswords[path] = password;
            }

            _pdfFiles.Add(new FileItem(path));
        }

        SetStatus(_mode == ConversionMode.PdfTools
            ? $"{_pdfFiles.Count}件のPDFが選択されています。結合時は一覧の順番どおりに並びます。"
            : $"{_pdfFiles.Count}件のPDFが選択されています。");

        RefreshPdfToolPreview();
    }

    private static bool RequiresPdfPassword(string pdfPath)
    {
        try
        {
            using var stream = File.OpenRead(pdfPath);
            _ = Conversion.GetPageCount(stream);
            return false;
        }
        catch (Exception ex)
        {
            return FriendlyErrorFormatter.IsPasswordError(ex);
        }
    }

    private async Task<string?> PromptForPdfPasswordAsync(string pdfPath)
    {
        var foreground = new SolidColorBrush(Color.Parse(_isDarkTheme ? "#F2F2F2" : "#202020"));
        var muted = new SolidColorBrush(Color.Parse(_isDarkTheme ? "#B8B8B8" : "#606060"));
        var background = new SolidColorBrush(Color.Parse(_isDarkTheme ? "#1D1D1D" : "#F2F2F2"));
        var fieldBackground = new SolidColorBrush(Color.Parse(_isDarkTheme ? "#2D2D2D" : "#FFFFFF"));
        var fieldBorder = new SolidColorBrush(Color.Parse(_isDarkTheme ? "#606060" : "#BEBEBE"));

        var passwordBox = new TextBox
        {
            PasswordChar = '●',
            Watermark = "PDFパスワード",
            Background = fieldBackground,
            Foreground = foreground,
            BorderBrush = fieldBorder,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var errorText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#D93F3F")),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };
        var openButton = new Button
        {
            Content = "開く",
            MinWidth = 92,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        var cancelButton = new Button
        {
            Content = "キャンセル",
            MinWidth = 92,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        var dialog = new Window
        {
            Title = "PDFパスワード",
            Width = 440,
            Height = 265,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = background
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancelButton, openButton }
        };
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "パスワードで保護されたPDFです",
                    FontSize = 20,
                    FontWeight = FontWeight.Bold,
                    Foreground = foreground
                },
                new TextBlock
                {
                    Text = Path.GetFileName(pdfPath),
                    Foreground = muted,
                    TextWrapping = TextWrapping.Wrap
                },
                passwordBox,
                errorText,
                buttonPanel
            }
        };

        void TryOpen()
        {
            var password = passwordBox.Text ?? string.Empty;
            if (string.IsNullOrEmpty(password))
            {
                errorText.Text = "パスワードを入力してください。";
                errorText.IsVisible = true;
                return;
            }

            try
            {
                using var stream = File.OpenRead(pdfPath);
                _ = Conversion.GetPageCount(stream, password: password);
                dialog.Close(password);
            }
            catch (Exception ex)
            {
                errorText.Text = FriendlyErrorFormatter.IsPasswordError(ex)
                    ? "パスワードが正しくありません。"
                    : FriendlyErrorFormatter.ToUserMessage(ex);
                errorText.IsVisible = true;
                passwordBox.SelectAll();
                passwordBox.Focus();
            }
        }

        openButton.Click += (_, _) => TryOpen();
        cancelButton.Click += (_, _) => dialog.Close(null);
        passwordBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                TryOpen();
                e.Handled = true;
            }
        };
        dialog.Opened += (_, _) => passwordBox.Focus();
        return await dialog.ShowDialog<string?>(this);
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
        return PdfDpiValues[_pdfDpiIndex];
    }

    private IReadOnlyDictionary<string, string> GetPdfPasswords()
    {
        return new Dictionary<string, string>(_pdfPasswords, StringComparer.OrdinalIgnoreCase);
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

        if (value.Contains("選択ページのみ出力", StringComparison.Ordinal)
            || value.Contains("ページ抽出", StringComparison.Ordinal))
        {
            return PdfToolOperation.ExtractPages;
        }

        if (value.Contains("テキスト出力", StringComparison.Ordinal)
            || value.Contains("文字を抽出", StringComparison.Ordinal))
        {
            return PdfToolOperation.ExtractText;
        }

        if (value.Contains("文字・テキスト", StringComparison.Ordinal)
            || value.Contains("テキスト追加", StringComparison.Ordinal)
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
        _pdfToolOutputPdfPanel.IsVisible = operation is PdfToolOperation.Merge
            or PdfToolOperation.DeletePages
            or PdfToolOperation.ExtractPages
            or PdfToolOperation.SimpleEdit
            or PdfToolOperation.ExtractText;
        _pdfToolOutputFolderPanel.IsVisible = operation == PdfToolOperation.Split;
        _pdfPageSelectionPanel.IsVisible = operation is PdfToolOperation.DeletePages or PdfToolOperation.ExtractPages;
        _pdfSimpleEditPanel.IsVisible = operation == PdfToolOperation.SimpleEdit;
        _pdfTextOutputPanel.IsVisible = operation == PdfToolOperation.ExtractText;
        _pdfToolOutputPdfLabel.Text = operation switch
        {
            PdfToolOperation.DeletePages => "削除後のPDF",
            PdfToolOperation.ExtractPages => "選択ページのPDF",
            PdfToolOperation.SimpleEdit => "編集後のPDF",
            PdfToolOperation.ExtractText => "出力テキスト（TXT）",
            _ => "結合後のPDF"
        };
        _pdfToolOutputPdfTextBox.Watermark = operation == PdfToolOperation.ExtractText
            ? "保存先とTXT名"
            : "保存先とPDF名";
        _pdfPageSelectionLabel.Text = operation == PdfToolOperation.ExtractPages
            ? "出力するページ"
            : "削除するページ";
        _pdfPageSelectionHelpText.Text = operation == PdfToolOperation.ExtractPages
            ? "選択したページを元の順番で1つのPDFにまとめます。"
            : "ページ番号はPDFの先頭を1ページ目として指定します。";

        var defaultFileName = operation switch
        {
            PdfToolOperation.DeletePages => "deleted_pages.pdf",
            PdfToolOperation.ExtractPages => "extracted_pages.pdf",
            PdfToolOperation.SimpleEdit => "edited.pdf",
            PdfToolOperation.ExtractText => "extracted_text.txt",
            _ => "merged.pdf"
        };
        var currentFileName = Path.GetFileName(_pdfToolOutputPdfTextBox.Text ?? string.Empty);
        var knownDefaultNames = new[]
        {
            "merged.pdf",
            "deleted_pages.pdf",
            "extracted_pages.pdf",
            "edited.pdf",
            "extracted_text.txt"
        };
        if (knownDefaultNames.Contains(currentFileName, StringComparer.OrdinalIgnoreCase)
            && !string.Equals(currentFileName, defaultFileName, StringComparison.OrdinalIgnoreCase))
        {
            _pdfToolOutputPdfTextBox.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                defaultFileName);
            _pdfToolOutputPdfBaseNameTextBox.Text = Path.GetFileNameWithoutExtension(defaultFileName);
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
        var isPageSelectionMode = operation is PdfToolOperation.DeletePages
            or PdfToolOperation.ExtractPages
            or PdfToolOperation.ExtractText;
        var selectionLabel = operation == PdfToolOperation.ExtractText
            ? "ページ全体"
            : operation == PdfToolOperation.ExtractPages ? "出力" : "削除";
        _pdfPreviewHelpText.Text = operation == PdfToolOperation.DeletePages
            ? "削除したいページにチェックを入れると、ページ番号が自動入力されます。"
            : operation == PdfToolOperation.ExtractPages
                ? "出力したいページにチェックを入れると、ページ番号が自動入力されます。"
            : operation == PdfToolOperation.SimpleEdit
                ? "編集したいページ上をクリックすると、右側に位置が自動入力されます。"
            : operation == PdfToolOperation.ExtractText
                ? "ページ全体はチェック、細かな範囲はページ画像を開いて文字の上をなぞります。何も選択しない場合は全文を出力します。"
            : "結合や分割の前に、ページの見た目と順番を確認できます。";

        try
        {
            var pdfPaths = _pdfFiles.Select(item => item.Path).ToList();
            var passwords = GetPdfPasswords();
            var previews = await Task.Run(
                () => CreatePdfPagePreviews(pdfPaths, passwords, isPageSelectionMode, selectionLabel, token),
                token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            foreach (var preview in previews)
            {
                if (operation == PdfToolOperation.ExtractText
                    && _pdfTextSelections.ContainsKey((preview.PdfPath, preview.PageNumber)))
                {
                    preview.HasEditMarker = true;
                    preview.EditMarkerLeft = 108;
                    preview.EditMarkerTop = 6;
                }
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
        _pdfPreviewIconButton.IsChecked = !_isPdfPreviewListView;
        _pdfPreviewListButton.IsChecked = _isPdfPreviewListView;
        _pdfPreviewSelectionTransform.X = _isPdfPreviewListView ? PreviewViewDragMaximum : 0;
    }

    private static List<PdfPagePreviewItem> CreatePdfPagePreviews(
        IReadOnlyList<string> pdfPaths,
        IReadOnlyDictionary<string, string> passwords,
        bool isPageSelectionMode,
        string selectionLabel,
        CancellationToken cancellationToken)
    {
        var previews = new List<PdfPagePreviewItem>();
        var options = new PDFtoImage.RenderOptions(
            Dpi: 110,
            WithAnnotations: true,
            BackgroundColor: SKColors.White,
            UseTiling: true);

        foreach (var pdfPath in pdfPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = File.OpenRead(pdfPath);
            var password = passwords.TryGetValue(pdfPath, out var value) ? value : string.Empty;
            var pageNumber = 0;

            foreach (var bitmap in Conversion.ToImages(
                         stream,
                         password: string.IsNullOrEmpty(password) ? null : password,
                         options: options))
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
                        isPageSelectionMode,
                        selectionLabel));
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

    private static string NormalizeOptionalColorHex(string? colorHex)
    {
        return IsNoColor(colorHex) ? "None" : NormalizeColorHex(colorHex);
    }

    private static bool IsNoColor(string? colorHex)
    {
        return string.Equals(colorHex?.Trim(), "None", StringComparison.OrdinalIgnoreCase)
            || string.Equals(colorHex?.Trim(), "Transparent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(colorHex?.Trim(), "なし", StringComparison.OrdinalIgnoreCase)
            || string.Equals(colorHex?.Trim(), "色なし", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLineShape(PdfShapeEditDraft shape)
    {
        return shape.ShapeType is "Line" or "HorizontalLine";
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

    private static string EnsureTextExtension(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            ? path
            : $"{path}.txt";
    }

    private static string BuildOutputPdfPath(
        string currentPath,
        string outputBaseName,
        IReadOnlyList<string> sourcePaths)
    {
        var path = EnsurePdfExtension(currentPath);
        if (string.IsNullOrWhiteSpace(outputBaseName))
        {
            return FileNameHelper.IncludeSourceNamesInPath(path, sourcePaths);
        }

        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        var desiredPath = Path.Combine(directory, $"{FileNameHelper.SafeBaseName(outputBaseName)}.pdf");
        return FileNameHelper.IncludeSourceNamesInPath(desiredPath, sourcePaths);
    }

    private static string BuildOutputTextPath(
        string currentPath,
        string outputBaseName,
        IReadOnlyList<string> sourcePaths)
    {
        var path = EnsureTextExtension(currentPath);
        if (string.IsNullOrWhiteSpace(outputBaseName))
        {
            return FileNameHelper.IncludeSourceNamesInPath(path, sourcePaths);
        }

        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        var desiredPath = Path.Combine(directory, $"{FileNameHelper.SafeBaseName(outputBaseName)}.txt");
        return FileNameHelper.IncludeSourceNamesInPath(desiredPath, sourcePaths);
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
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#702D2D2D")),
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F2F2F2")),
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
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1D1D1D")),
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
                            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F2F2F2")),
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

    public string BackgroundColorHex { get; set; } = "None";

    public string TextColorHex { get; set; } = "#000000";

    public string TextAlignment { get; set; } = "Left";

    public bool IsBold { get; set; }

    public bool IsUnderline { get; set; }

    public string DisplayText => string.IsNullOrWhiteSpace(Text) ? "(空のテキスト)" : Text;
}

internal sealed record PdfTextSelectionState(
    IReadOnlyList<int> SelectedWordIndices,
    string Text);

internal sealed class PdfShapeEditDraft
{
    public int PageNumber { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public string ShapeType { get; set; } = "Rectangle";

    public string FillColorHex { get; set; } = "None";

    public string StrokeColorHex { get; set; } = "#000000";

    public double StrokeThickness { get; set; } = 2;

    public double CornerRadius { get; set; }

    public double RotationDegrees { get; set; }
}

#pragma warning restore CA1416
