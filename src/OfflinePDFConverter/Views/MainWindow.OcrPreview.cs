using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Interactivity;
using OfflinePDFConverter.Models;
using OfflinePDFConverter.Services;
using UglyToad.PdfPig;

namespace OfflinePDFConverter.Views;

public partial class MainWindow
{
    private async Task OpenOcrPreviewWindowAsync(PdfPagePreviewItem item, bool after)
    {
        var password = !after && _pdfPasswords.TryGetValue(item.PdfPath, out var saved) ? saved : "";
        var data = await Task.Run(() =>
        {
            using var pdf = PdfDocument.Open(item.PdfPath, new ParsingOptions { Password = password });
            var page = pdf.GetPage(item.PageNumber);
            var characters = new List<PdfSelectableWord>();
            var wordIndex = 0;
            if (after) foreach (var word in page.GetWords())
            {
                foreach (var letter in word.Letters)
                {
                    var box = letter.BoundingBox;
                    if (string.IsNullOrWhiteSpace(letter.Value)) continue;
                    characters.Add(new(characters.Count, wordIndex, letter.Value,
                        box.Left, page.Height - box.Top, Math.Max(1, box.Width), Math.Max(1, box.Height)));
                }
                wordIndex++;
            }
            return (Characters: characters, Width: page.Width, Height: page.Height);
        });
        var characters = data.Characters;
        var selected = new HashSet<int>();
        var highlights = new List<Rectangle>();
        var zoom = 1.0;
        var baseWidth = 720.0;
        var scale = baseWidth / data.Width;
        var canvas = new Canvas { Width = baseWidth, Height = data.Height * scale, Background = Brushes.White, Focusable = true, Cursor = new Cursor(after ? StandardCursorType.Ibeam : StandardCursorType.Arrow) };
        var image = new Image { Source = item.Thumbnail, Width = canvas.Width, Height = canvas.Height, Stretch = Stretch.Fill };
        canvas.Children.Add(image);
        var scroll = new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Margin = new Thickness(16) };
        var selectedText = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = 120, Watermark = "選択した文字がここに表示されます。" };
        var copy = new Button { Content = "選択した文字をコピー", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Stretch };
        var count = new TextBlock { Text = "文字をドラッグして選択", TextWrapping = TextWrapping.Wrap };
        var zoomLabel = new TextBlock { Text = "100%", VerticalAlignment = VerticalAlignment.Center, Width = 50, TextAlignment = TextAlignment.Center };
        var minus = new Button { Content = "−" }; var plus = new Button { Content = "＋" };
        var reset = new Button { Content = "100%" };
        var all = new Button { Content = "すべて選択", IsVisible = after, IsEnabled = characters.Count > 0 };
        var clear = new Button { Content = "選択を解除", IsVisible = after };
        var close = new Button { Content = "閉じる", HorizontalAlignment = HorizontalAlignment.Stretch };
        var background = new SolidColorBrush(Color.Parse(_isDarkTheme ? "#191919" : "#F2F2F2"));
        var panel = new SolidColorBrush(Color.Parse(_isDarkTheme ? "#222222" : "#FAFAFA"));
        var dialog = new Window { Title = $"{(after ? "処理後" : "処理前")} · {item.Title} · {item.PageLabel}", Width = 1120, Height = 800,
            MinWidth = 760, MinHeight = 520, Background = background, RequestedThemeVariant = RequestedThemeVariant,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 12),
            Children = { minus, zoomLabel, plus, reset, all, clear } };
        var sidebar = new StackPanel { Spacing = 12 };
        sidebar.Children.Add(new TextBlock {
            Text = after ? "処理後の文字を確認" : "処理前のPDFを確認",
            FontSize = 20, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        if (after)
        {
            sidebar.Children.Add(new TextBlock {
                Text = characters.Count == 0 ? "このページには選択できる文字がありません。"
                    : "PDF内の文字をドラッグして選択できます。Ctrl/Cmdを押しながらドラッグで追加、Shiftで範囲を拡張します。",
                TextWrapping = TextWrapping.Wrap });
            sidebar.Children.Add(count);
            sidebar.Children.Add(selectedText);
            sidebar.Children.Add(copy);
            sidebar.Children.Add(new TextBlock {
                Text = "Ctrl+C / ⌘C：コピー\nCtrl+A / ⌘A：すべて選択\nEsc：選択を解除\nCtrl/Cmd＋ホイール：拡大・縮小",
                TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        }
        sidebar.Children.Add(close);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,280"), RowDefinitions = new RowDefinitions("Auto,*") };
        grid.Children.Add(toolbar); Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        var side = new Border { Background = panel, CornerRadius = new CornerRadius(18), Padding = new Thickness(16), Margin = new Thickness(0,16,16,16),
            Child = new ScrollViewer { Content = sidebar } };
        Grid.SetColumn(side, 1); Grid.SetRowSpan(side, 2); grid.Children.Add(side); dialog.Content = grid;
        if (after)
        {
            // Match the text editor's workspace, floating zoom panel and fixed completion button.
            dialog.Width = 900; dialog.Height = 720;
            var muted = new SolidColorBrush(Color.Parse(_isDarkTheme ? "#B8B8B8" : "#606060"));
            foreach (var label in sidebar.Children.OfType<TextBlock>())
                label.Foreground = new SolidColorBrush(Color.Parse(_isDarkTheme ? "#F2F2F2" : "#202020"));
            toolbar.Children.Clear();
            ((ScrollViewer)side.Child!).Content = null;
            side.Child = null; grid.Children.Clear(); Grid.SetRow(scroll, 0);
            sidebar.Children.Remove(close);
            sidebar.Children.Insert(2, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { all, clear } });
            copy.Classes.Add("small-action"); all.Classes.Add("small-action"); clear.Classes.Add("small-action");
            selectedText.Background = _isDarkTheme ? new SolidColorBrush(Color.Parse("#2D2D2D")) : Brushes.White;
            selectedText.BorderBrush = new SolidColorBrush(Color.Parse(_isDarkTheme ? "#606060" : "#BEBEBE"));
            foreach (var button in new[] { minus, plus })
            {
                button.Classes.Add("small-action"); button.MinWidth = 42; button.MinHeight = 38;
                button.FontSize = 22; button.FontWeight = FontWeight.Bold; button.Padding = new Thickness(0);
                button.HorizontalContentAlignment = HorizontalAlignment.Center;
                button.VerticalContentAlignment = VerticalAlignment.Center;
            }
            scroll.Margin = new Thickness(0);
            scroll.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden;
            scroll.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden;
            var zoomPanel = new Border {
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(10), CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(_isDarkTheme ? Color.FromArgb(190, 29, 32, 38) : Color.FromArgb(190, 248, 251, 255)),
                BorderBrush = new SolidColorBrush(_isDarkTheme ? Color.FromArgb(140, 120, 130, 145) : Color.FromArgb(150, 150, 170, 190)),
                BorderThickness = new Thickness(1),
                Child = new StackPanel { Spacing = 6, Children = {
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Center, Children = { minus, plus } },
                    zoomLabel,
                    new TextBlock { Text = "ピンチ操作、またはCtrl / ⌘ + マウスホイールでも拡大縮小できます",
                        FontSize = 10, Foreground = muted, TextWrapping = TextWrapping.Wrap, MaxWidth = 150 }
                } }
            };
            close.Content = "完了"; close.Classes.Add("primary"); close.FontSize = 28;
            close.FontWeight = FontWeight.Bold; close.MinHeight = 66; close.Padding = new Thickness(24, 12);
            close.CornerRadius = new CornerRadius(999); close.Margin = new Thickness(0, 12, 0, 0);
            close.HorizontalContentAlignment = HorizontalAlignment.Center;
            close.VerticalContentAlignment = VerticalAlignment.Center;
            var rightPanel = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Background = panel,
                Children = { new ScrollViewer { Content = sidebar,
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden }, close } };
            Grid.SetRow(close, 1); Grid.SetColumn(rightPanel, 1);
            dialog.Content = new Grid { Margin = new Thickness(20), ColumnDefinitions = new ColumnDefinitions("*,260"),
                ColumnSpacing = 18, Children = { new Grid { Children = { scroll, zoomPanel } }, rightPanel } };
        }

        string BuildText()
        {
            var text = new System.Text.StringBuilder();
            PdfSelectableWord? previous = null;
            foreach (var character in characters.Where(c => selected.Contains(c.Index)))
            {
                if (previous != null && (previous.WordIndex != character.WordIndex || character.Index != previous.Index + 1))
                {
                    var differentLine = Math.Abs(character.Top - previous.Top) > Math.Max(character.Height, previous.Height) * .6;
                    text.Append(differentLine ? Environment.NewLine : " ");
                }
                text.Append(character.Text); previous = character;
            }
            return text.ToString();
        }
        void Redraw()
        {
            foreach (var rect in highlights) canvas.Children.Remove(rect);
            highlights.Clear();
            foreach (var character in characters.Where(c => selected.Contains(c.Index)))
            {
                var rect = new Rectangle { Width = character.Width * scale * zoom, Height = character.Height * scale * zoom,
                    Fill = new SolidColorBrush(Color.FromArgb(90, 65, 140, 240)), IsHitTestVisible = false };
                Canvas.SetLeft(rect, character.Left * scale * zoom); Canvas.SetTop(rect, character.Top * scale * zoom);
                highlights.Add(rect); canvas.Children.Add(rect);
            }
            selectedText.Text = BuildText(); copy.IsEnabled = selected.Count > 0;
            count.Text = selected.Count == 0 ? "文字をドラッグして選択" : $"{selected.Count}文字を選択中";
        }
        void SetZoom(double value)
        {
            zoom = Math.Clamp(value, .25, 4);
            canvas.Width = image.Width = baseWidth * zoom; canvas.Height = image.Height = data.Height * scale * zoom;
            zoomLabel.Text = $"{zoom:P0}"; Redraw();
        }
        async Task CopyAsync()
        {
            if (selected.Count == 0 || dialog.Clipboard == null) return;
            try
            {
                await dialog.Clipboard.SetTextAsync(BuildText()); count.Text = "選択した文字をコピーしました。";
            }
            catch (Exception ex) { count.Text = "コピーできませんでした：" + FriendlyErrorFormatter.ToUserMessage(ex); }
        }
        minus.Click += (_, _) => SetZoom(zoom - .25); plus.Click += (_, _) => SetZoom(zoom + .25); reset.Click += (_, _) => SetZoom(1);
        close.Click += (_, _) => dialog.Close();
        if (!after)
        {
            await dialog.ShowDialog(this);
            return;
        }
        all.Click += (_, _) => { selected = characters.Select(c => c.Index).ToHashSet(); Redraw(); };
        clear.Click += (_, _) => { selected.Clear(); Redraw(); };
        copy.Click += async (_, _) => await CopyAsync();
        Point start = default; int? pressed = null; int? anchor = null; HashSet<int> previous = new();
        bool dragging = false, additive = false, extend = false;
        Point ToPage(Point point) => new(point.X / (scale * zoom), point.Y / (scale * zoom));
        canvas.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
            canvas.Focus(); start = ToPage(e.GetPosition(canvas)); pressed = PdfTextSelectionLogic.HitCharacter(characters, start);
            previous = new(selected); additive = PdfTextSelectionLogic.IsAdditiveModifier(e.KeyModifiers, OperatingSystem.IsMacOS());
            extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift); dragging = true; e.Pointer.Capture(canvas); e.Handled = true;
        };
        void Apply(Point point)
        {
            selected = PdfTextSelectionLogic.ApplyGesture(characters, previous, pressed,
                PdfTextSelectionLogic.FindCharacter(characters, point), anchor,
                PdfTextSelectionLogic.IsDrag(start, point, scale * zoom), additive, extend).ToHashSet(); Redraw();
        }
        canvas.PointerMoved += (_, e) => { if (dragging) { Apply(ToPage(e.GetPosition(canvas))); e.Handled = true; } };
        canvas.PointerReleased += (_, e) =>
        {
            if (!dragging) return;
            Apply(ToPage(e.GetPosition(canvas))); if (!extend && pressed != null) anchor = pressed;
            dragging = false; e.Pointer.Capture(null); e.Handled = true;
        };
        canvas.PointerCaptureLost += (_, _) => dragging = false;
        scroll.PointerWheelChanged += (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Meta)) return;
            SetZoom(zoom + e.Delta.Y * .1); e.Handled = true;
        };
        Gestures.AddPointerTouchPadGestureMagnifyHandler(canvas, (_, e) =>
        {
            var delta = Math.Abs(e.Delta.Y) >= Math.Abs(e.Delta.X) ? e.Delta.Y : e.Delta.X;
            if (Math.Abs(delta) > .001) SetZoom(zoom + (delta > 0 ? .08 : -.08));
            e.Handled = true;
        });
        dialog.AddHandler(KeyDownEvent, async (_, e) =>
        {
            var shortcut = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (shortcut && e.Key == Key.C) { await CopyAsync(); e.Handled = true; }
            else if (shortcut && e.Key == Key.A) { selected = characters.Select(c => c.Index).ToHashSet(); Redraw(); e.Handled = true; }
            else if (e.Key == Key.Escape) { selected.Clear(); Redraw(); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        await dialog.ShowDialog(this);
    }
}
