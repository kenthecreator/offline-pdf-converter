using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using OfflinePDFConverter.Services;
using PDFtoImage;
using SkiaSharp;

namespace OfflinePDFConverter.Views;

#pragma warning disable CA1416 // PDFtoImage supports the target desktop platforms.

internal sealed class PdfPageEditWindow : Window
{
    private const double PreviewWidth = 566;
    private const double PreviewHeight = 452;
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#1670D2"));

    private readonly string _source;
    private readonly string? _password;
    private readonly double[] _pageAspects;
    private int PageCount => _pageAspects.Length;
    private readonly bool _layoutMode;
    private readonly int[] _rotations;
    private readonly Canvas _canvas = new() { Background = Brushes.White, ClipToBounds = true };
    private readonly ComboBox _count = new()
    {
        ItemsSource = PdfLayout.PresetPageCounts.Select(count => $"{count} in 1").ToArray(),
        SelectedIndex = 0,
        Width = 120
    };
    private readonly ComboBox _orientation = new() { ItemsSource = new[] { "A4 横", "A4 縦" }, SelectedIndex = 0, Width = 100 };
    private readonly CheckBox _allPages = new() { Content = "全ページを回転" };
    private readonly Button _previous = new() { Content = "前へ" };
    private readonly Button _next = new() { Content = "次へ" };
    private readonly Button _save = new() { Content = "PDFを保存", IsEnabled = false };
    private readonly Button _left = new() { Content = "左に90°" };
    private readonly Button _right = new() { Content = "右に90°" };
    private readonly Button _reset = new() { Content = "この用紙の配置をリセット" };
    private readonly TextBlock _position = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<int, Bitmap> _images = new();
    private readonly Dictionary<int, Border> _tiles = new();
    private PdfLayout _layout;
    private int _index;
    private int? _selected;
    private double _previewScale;
    private Point _dragStart;
    private PagePlacement _dragPlacement;
    private bool _resizing;
    private bool _loading;
    private bool _closed;

    public PdfPageEditWindow(string source, string? password, double[] pageAspects, bool layoutMode)
    {
        _source = source;
        _password = password;
        _pageAspects = pageAspects;
        _layoutMode = layoutMode;
        _rotations = new int[PageCount];
        _layout = new PdfLayout(PageCount, 2, landscape: true);
        Title = layoutMode ? "複数ページのレイアウト編集" : "ページを回転";
        Width = 660;
        Height = 665;
        MinWidth = 400;
        MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center, Children = { _previous, _position, _next } };
        var settings = new WrapPanel { Orientation = Orientation.Horizontal };
        if (layoutMode)
        {
            settings.Children.Add(_count);
            settings.Children.Add(_orientation);
            settings.Children.Add(_reset);
            _status.Text = "ページをドラッグして移動し、右下の青いハンドルで拡大・縮小できます。";
        }
        else
        {
            settings.Children.Add(_left);
            settings.Children.Add(_right);
            settings.Children.Add(_allPages);
            _status.Text = "表示中のページを回転できます。「全ページを回転」でまとめて回転します。";
        }
        foreach (var control in settings.Children)
            control.Margin = new Thickness(0, 0, 8, 4);

        var preview = new Border { Background = new SolidColorBrush(Color.Parse("#545960")),
            Padding = new Thickness(12), Child = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = _canvas } };
        var cancel = new Button { Content = "キャンセル", IsCancel = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Children = { cancel, _save } };
        var body = new Grid { Margin = new Thickness(18), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"), RowSpacing = 10 };
        body.Children.Add(settings);
        Grid.SetRow(navigation, 1); body.Children.Add(navigation);
        Grid.SetRow(preview, 2); body.Children.Add(preview);
        Grid.SetRow(_status, 3); body.Children.Add(_status);
        Grid.SetRow(actions, 4); body.Children.Add(actions);
        Content = body;

        Opened += async (_, _) => await LoadPreviewAsync();
        Closed += (_, _) => { _closed = true; ClearPreview(); };
        _previous.Click += async (_, _) => { _index--; await LoadPreviewAsync(); };
        _next.Click += async (_, _) => { _index++; await LoadPreviewAsync(); };
        _count.SelectionChanged += async (_, _) => await ResetLayoutAsync();
        _orientation.SelectionChanged += async (_, _) => await ResetLayoutAsync();
        _left.Click += (_, _) => ChangeRotation(-90);
        _right.Click += (_, _) => ChangeRotation(90);
        _reset.Click += (_, _) => { _layout.ResetSheet(_index); DrawLayout(); };
        _save.Click += (_, _) => Close(true);
        cancel.Click += (_, _) => Close(false);
    }

    public PdfLayout Layout => _layout;
    public IReadOnlyList<int> Rotations => _rotations;

    private async Task ResetLayoutAsync()
    {
        if (_loading || _closed || _count.SelectedIndex < 0)
            return;
        _layout = new PdfLayout(PageCount, PdfLayout.PresetPageCounts[_count.SelectedIndex], _orientation.SelectedIndex == 0);
        _index = 0;
        await LoadPreviewAsync();
    }

    private void ClearPreview()
    {
        _canvas.Children.Clear();
        _tiles.Clear();
        _selected = null;
        foreach (var bitmap in _images.Values)
            bitmap.Dispose();
        _images.Clear();
    }

    private async Task LoadPreviewAsync()
    {
        _loading = true;
        ClearPreview();
        UpdateControls(previewReady: false);
        var indices = _layoutMode ? _layout.PagesOnSheet(_index).ToArray() : new[] { _index };
        try
        {
            foreach (var index in indices)
            {
                var bitmap = await Task.Run(() => RenderPage(index));
                if (_closed)
                {
                    bitmap.Dispose();
                    return;
                }
                _images.Add(index, bitmap);
            }

            if (_layoutMode) DrawLayout();
            else DrawRotation();
            _status.Text = _layoutMode
                ? "ページをドラッグして移動し、右下の青いハンドルで拡大・縮小できます。枚数や用紙の向きを変えると配置をリセットします。"
                : "表示中のページを回転できます。「全ページを回転」でまとめて回転します。";
        }
        catch (Exception ex)
        {
            if (!_closed)
                _status.Text = $"プレビューを表示できませんでした。{FriendlyErrorFormatter.ToUserMessage(ex)}";
        }
        finally
        {
            _loading = false;
            if (!_closed)
                UpdateControls(_images.Count == indices.Length);
        }
    }

    private Bitmap RenderPage(int page)
    {
        using var file = File.OpenRead(_source);
        const int previewPixels = 850;
        var landscape = _pageAspects[page] >= 1;
        var options = new PDFtoImage.RenderOptions(
            Width: landscape ? previewPixels : null, Height: landscape ? null : previewPixels,
            WithAspectRatio: true, WithAnnotations: !_layoutMode, BackgroundColor: SKColors.White, UseTiling: true);
        using var bitmap = Conversion.ToImage(file, page, password: _password, options: options);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }

    private void UpdateControls(bool previewReady)
    {
        var total = _layoutMode ? _layout.SheetCount : PageCount;
        _position.Text = $"{(_layoutMode ? "用紙" : "ページ")} {_index + 1} / {total}";
        _previous.IsEnabled = !_loading && _index > 0;
        _next.IsEnabled = !_loading && _index + 1 < total;
        _count.IsEnabled = _orientation.IsEnabled = !_loading;
        _left.IsEnabled = _right.IsEnabled = _reset.IsEnabled = previewReady;
        _save.IsEnabled = previewReady && (_layoutMode || _rotations.Any(angle => angle != 0));
    }

    private void ChangeRotation(int angle)
    {
        if (_allPages.IsChecked == true)
        {
            for (var index = 0; index < _rotations.Length; index++)
                _rotations[index] = PdfPageEditingService.NormalizeRotation(_rotations[index] + angle);
        }
        else
        {
            _rotations[_index] = PdfPageEditingService.NormalizeRotation(_rotations[_index] + angle);
        }
        DrawRotation();
        UpdateControls(previewReady: true);
    }

    private void DrawRotation()
    {
        _canvas.Children.Clear();
        var bitmap = _images[_index];
        var angle = _rotations[_index];
        var quarterTurn = angle is 90 or 270;
        var width = bitmap.Size.Width;
        var height = bitmap.Size.Height;
        var scale = Math.Min(PreviewWidth / (quarterTurn ? height : width), PreviewHeight / (quarterTurn ? width : height));
        _canvas.Width = (quarterTurn ? height : width) * scale;
        _canvas.Height = (quarterTurn ? width : height) * scale;
        var image = new Image
        {
            Source = bitmap, Width = width * scale, Height = height * scale,
            RenderTransform = new RotateTransform(angle)
        };
        Canvas.SetLeft(image, (_canvas.Width - image.Width) / 2);
        Canvas.SetTop(image, (_canvas.Height - image.Height) / 2);
        _canvas.Children.Add(image);
    }

    private void DrawLayout()
    {
        _canvas.Children.Clear();
        _tiles.Clear();
        _selected = null;
        _previewScale = Math.Min(PreviewWidth / _layout.Width, PreviewHeight / _layout.Height);
        _canvas.Width = _layout.Width * _previewScale;
        _canvas.Height = _layout.Height * _previewScale;

        foreach (var (page, bitmap) in _images)
        {
            var handle = new Border
            {
                Width = 12, Height = 12, Background = Accent, IsVisible = false,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Cursor = new Cursor(StandardCursorType.BottomRightCorner)
            };
            var tile = new Border
            {
                Cursor = new Cursor(StandardCursorType.SizeAll),
                Background = Brushes.White,
                Child = new Grid
                {
                    Children =
                    {
                        new Image { Source = bitmap, Stretch = Stretch.Fill, IsHitTestVisible = false },
                        new Border { BorderThickness = new Thickness(1), BorderBrush = Brushes.LightGray, IsHitTestVisible = false },
                        handle
                    }
                }
            };
            ToolTip.SetTip(tile, $"元PDFの {page + 1} ページ");
            tile.PointerPressed += (_, e) => BeginDrag(page, tile, handle, e);
            tile.PointerMoved += (_, e) => MoveDrag(page, tile, e);
            tile.PointerReleased += (_, e) => { if (e.Pointer.Captured == tile) e.Pointer.Capture(null); };
            _tiles.Add(page, tile);
            _canvas.Children.Add(tile);
            PositionTile(tile, Placement(page));
        }
    }

    private PagePlacement Placement(int page) => _layout.GetPlacement(page, _pageAspects[page]);

    private void PositionTile(Border tile, PagePlacement placement)
    {
        tile.Width = placement.Width * _previewScale;
        tile.Height = placement.Height * _previewScale;
        Canvas.SetLeft(tile, placement.X * _previewScale);
        Canvas.SetTop(tile, placement.Y * _previewScale);
    }

    private void BeginDrag(int page, Border tile, Border handle, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed)
            return;

        _resizing = ReferenceEquals(e.Source, handle);
        if (_selected is int previous && _tiles[previous].Child is Grid previousContent)
        {
            ((Border)previousContent.Children[1]).BorderBrush = Brushes.LightGray;
            previousContent.Children[2].IsVisible = false;
        }
        _selected = page;
        var content = (Grid)tile.Child!;
        ((Border)content.Children[1]).BorderBrush = Accent;
        handle.IsVisible = true;
        _dragStart = e.GetPosition(_canvas);
        _dragPlacement = Placement(page);
        e.Pointer.Capture(tile);
        e.Handled = true;
    }

    private void MoveDrag(int page, Border tile, PointerEventArgs e)
    {
        if (e.Pointer.Captured != tile)
            return;

        var delta = (e.GetPosition(_canvas) - _dragStart) / _previewScale;
        var initial = _dragPlacement;
        PagePlacement placement;
        if (_resizing)
        {
            var maxScale = Math.Min((_layout.Width - initial.X) / initial.Width, (_layout.Height - initial.Y) / initial.Height);
            var minScale = Math.Min(maxScale, Math.Max(.15, 16 / Math.Max(initial.Width, initial.Height)));
            var direction = new Vector(initial.Width, initial.Height);
            var scale = Math.Clamp(1 + Vector.Dot(delta, direction) / direction.SquaredLength, minScale, maxScale);
            placement = initial with { Width = initial.Width * scale, Height = initial.Height * scale };
        }
        else
        {
            placement = initial with
            {
                X = Math.Clamp(initial.X + delta.X, 0, Math.Max(0, _layout.Width - initial.Width)),
                Y = Math.Clamp(initial.Y + delta.Y, 0, Math.Max(0, _layout.Height - initial.Height))
            };
        }
        _layout.SetPlacement(page, placement);
        PositionTile(tile, placement);
        e.Handled = true;
    }
}
