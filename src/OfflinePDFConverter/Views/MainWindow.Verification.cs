using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
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
        void SaveSnapshot(string path)
        {
            using var snapshot = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height), new Vector(96, 96));
            snapshot.Render(this);
            snapshot.Save(path);
        }
        void CheckSurfaceTone(SkiaSharp.SKBitmap image, Border surface, bool dark)
        {
            int ToneAt(double x)
            {
                var point = surface.TranslatePoint(new Point(x, surface.Bounds.Height / 2), this);
                Check(point.HasValue, "Surface is missing from the snapshot: " + surface.Name);
                var position = point!.Value;
                var pixel = image.GetPixel((int)position.X, (int)position.Y);
                return (pixel.Red + pixel.Green + pixel.Blue) / 3;
            }

            // Sample away from the empty-list hint and the native one-pixel border.
            int interior = ToneAt(16);
            int edge = ToneAt(4);
            string theme = dark ? "dark" : "light";
            Check(dark ? interior < 65 : interior > 220,
                $"Unexpected fill on {surface.Name} in {theme} theme: {interior}");
            Check(Math.Abs(interior - edge) < 24,
                $"Unexpected thick outline on {surface.Name} in {theme} theme: {edge} next to {interior}");
        }
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
            Program.TraceGuiVerification("Main window opened");
            Check(Title == AppIdentity.WindowTitle && IsVisible && Bounds.Width > 0, "Main window not shown.");
            Check(!string.IsNullOrWhiteSpace(DefaultOutputFolder) &&
                  _pdfOutputFolderTextBox.Text == DefaultOutputFolder &&
                  _pdfToolOutputFolderTextBox.Text == DefaultOutputFolder &&
                  Required<TextBox>("OcrOutputFolderTextBox").Text == DefaultOutputFolder &&
                  Path.GetDirectoryName(_imageOutputPdfTextBox.Text) == DefaultOutputFolder &&
                  Path.GetDirectoryName(_pdfToolOutputPdfTextBox.Text) == DefaultOutputFolder,
                "An output field did not default to Desktop.");
            checks.Add("all initial output folders and PDF paths default to Desktop");
            var glassEnabled = Program.GlassTrial == ModePillGlassTrial.On;
            foreach (var (pillName, trackName, _, _) in GlassSelectors)
            {
                Check(Required<Border>(trackName).Child is null, "The existing track must remain undecorated: " + trackName);
                var decoration = Required<Border>(pillName).Child;
                Check(glassEnabled ? decoration is { IsHitTestVisible: false, Focusable: false } : decoration is null,
                    "Incorrect glass decoration: " + pillName);
            }
            if (Program.GlassTrial == ModePillGlassTrial.On)
            {
                var modeLens = _selectorLenses["ModeSelectionPill"];
                await Until(() => !modeLens.IsAvailable || modeLens.HasRendered);
                Check(modeLens is { IsAvailable: true, HasRendered: true },
                    "Skia did not draw the mode selection glass.");
            }
            Check(ButtonGlass.IsAppliedTo(Required<Button>("StartPdfToolButton")) == glassEnabled,
                "Incorrect glass decoration on the main Start button.");
            foreach (var caption in new[] { "追加", "削除", "すべて削除", "上へ", "下へ", "選択" })
            {
                var action = this.GetVisualDescendants().OfType<Button>()
                    .FirstOrDefault(button => button.IsEffectivelyVisible && Equals(button.Content, caption));
                Check(action is not null, "Missing visible action button: " + caption);
                if (glassEnabled)
                    await Until(() => ButtonGlass.HasRenderedPill(action!));
                else
                    Check(!ButtonGlass.HasRenderedPill(action!), "Unexpected action button lens: " + caption);
            }
            async Task CheckGlassSurfaceAsync(Border border)
            {
                var rim = ButtonGlass.FindRim(border);
                Check(glassEnabled ? rim is { IsHitTestVisible: false, Focusable: false } : rim is null,
                    "Incorrect glass decoration on " + border.Name);
                if (rim != null)
                    await Until(() => rim.HasRendered);
            }
            foreach (var (mode, listName, comboName, fieldName, previewNames) in new[]
            {
                (ConversionMode.PdfTools, "PdfToolFilesList", "PdfToolOperationCombo", "PdfToolOutputPdfTextBox", new[] { "PdfPreviewSurface" }),
                (ConversionMode.PdfToImage, "PdfFilesList", "PdfFormatCombo", "PdfPageRangeTextBox", Array.Empty<string>()),
                (ConversionMode.ImageToPdf, "ImageFilesList", "ImagePageModeCombo", "ImageOutputPdfTextBox", Array.Empty<string>()),
                (ConversionMode.Ocr, "OcrFilesList", "", "OcrPagesTextBox", new[] { "OcrBeforePreviewSurface", "OcrAfterPreviewSurface" })
            })
            {
                SetMode(mode);
                var list = Required<ListBox>(listName);
                await Until(() => list.GetVisualDescendants().OfType<Border>().Any(border => border.Name == "border"));
                var listBorder = list.GetVisualDescendants().OfType<Border>().First(border => border.Name == "border");
                await CheckGlassSurfaceAsync(listBorder);
                Check(listBorder.GetVisualDescendants().OfType<ScrollViewer>().Any(viewer => viewer.Name == "PART_ScrollViewer"),
                    "The file list lost its original scroll viewer: " + listName);
                foreach (var name in previewNames)
                    await CheckGlassSurfaceAsync(Required<Border>(name));
                var panel = this.GetVisualDescendants().OfType<Border>()
                    .Single(border => border.IsEffectivelyVisible && border.Classes.Contains("settings-panel"));
                var firstCard = panel.GetVisualDescendants().OfType<Border>()
                    .First(border => border.IsEffectivelyVisible && border.Classes.Contains("settings-card"));
                await CheckGlassSurfaceAsync(panel);
                await CheckGlassSurfaceAsync(firstCard);
                if (ButtonGlass.FindRim(firstCard) is { } cardRim)
                    Check(cardRim.Bounds.Width >= firstCard.Bounds.Width - 4, "Card padding inset the optical rim.");
                var field = Required<TextBox>(fieldName);
                await Until(() => field.GetVisualDescendants().OfType<Border>().Any(border => border.Name == "PART_BorderElement"));
                await CheckGlassSurfaceAsync(field.GetVisualDescendants().OfType<Border>().First(border => border.Name == "PART_BorderElement"));
                if (mode == ConversionMode.Ocr)
                {
                    var choice = Required<RadioButton>("OcrHorizontalChoice");
                    await Until(() => !glassEnabled || choice.GetVisualDescendants().OfType<ButtonGlass>().Any(rim => rim.HasRendered));
                    Check(choice.IsChecked == true, "The checked language choice was lost.");
                }
                if (comboName.Length > 0)
                {
                    var combo = Required<ComboBox>(comboName);
                    await Until(() => combo.GetVisualDescendants().OfType<Border>().Any(border => border.Name == "Background"));
                    var face = combo.GetVisualDescendants().OfType<Border>().First(border => border.Name == "Background");
                    Check(face.CornerRadius.TopLeft >= face.Bounds.Height / 2, "Dropdown is not pill-shaped: " + comboName);
                    await CheckGlassSurfaceAsync(face);
                    combo.IsDropDownOpen = true;
                    await Until(() => combo.IsDropDownOpen && combo.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Any(popup => popup.IsOpen));
                    var popup = combo.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Single(item => item.IsOpen);
                    if (popup.Child is Border popupBorder)
                        await CheckGlassSurfaceAsync(popupBorder);
                    combo.IsDropDownOpen = false;
                }
                if (glassEnabled)
                {
                    foreach (int theme in new[] { 1, 2, 1 })
                    {
                        FinishThemeDrag(theme);
                        await Until(() => !_themeTransitionActive && _themeMode == theme);
                        bool dark = theme == 2;
                        var snapshotPath = Path.ChangeExtension(report, $".glass-{mode}-{(dark ? "dark" : "light")}.png");
                        SaveSnapshot(snapshotPath);
                        using var screenshot = SkiaSharp.SKBitmap.Decode(snapshotPath);
                        CheckSurfaceTone(screenshot, listBorder, dark);
                        foreach (var name in previewNames)
                            CheckSurfaceTone(screenshot, Required<Border>(name), dark);
                    }
                }
            }
            SetMode(ConversionMode.PdfTools);
            if (glassEnabled)
                checks.Add("drop areas and empty previews keep their theme colors and avoid thick outlines after repeated theme switches");
            checks.Add("pill dropdowns, original list scrolling, and glass on file-drop, preview and settings surfaces");
            checks.Add($"five selector pills and six labeled action buttons: {Program.GlassTrial}");
            var progressParts = _mainProgressBar.GetVisualDescendants().OfType<Border>().ToArray();
            var track = progressParts.Single(border => border.Name == "ProgressBarRoot");
            var indicator = progressParts.Single(border => border.Name == "PART_Indicator");
            var trackRim = (track.Child as Panel)?.Children.OfType<ButtonGlass>().SingleOrDefault();
            var fillRim = indicator.Child as ButtonGlass;
            Check((trackRim is not null) == glassEnabled && (fillRim is not null) == glassEnabled,
                "Incorrect glass decoration on the bottom progress bar.");
            _mainProgressBar.Value = 100;
            await Until(() => indicator.Bounds.Width >= track.Bounds.Width - 4);
            _mainProgressBar.Value = 0;
            await Until(() => indicator.Bounds.Width < 1);
            _mainProgressBar.Value = 42;
            await Until(() => Math.Abs(indicator.Bounds.Width - track.Bounds.Width * 0.42) < 4);
            if (glassEnabled)
                await Until(() => trackRim!.HasRendered && fillRim!.HasRendered);
            checks.Add("bottom progress bar width at 0%, 42% and 100%, with the correct glass state");
            var headerIcon = Required<Image>("AppHeaderIcon");
            var headerSource = headerIcon.Source;
            Check(headerSource != null && headerIcon.IsEffectivelyVisible, "Header icon is missing.");
            for (var theme = 0; theme < 3; theme++)
            {
                FinishThemeDrag(theme);
                await Until(() => !_themeTransitionActive);
                Check(_themeMode == theme, "Theme switch failed.");
                if (glassEnabled)
                {
                    await Until(() => Math.Abs(_themeToggleTransform.X - theme * ThemeSegmentWidth) < .01);
                    Check(Required<Border>("ThemeToggleThumb").ZIndex == Required<Grid>("ThemeSelectorLabels").ZIndex,
                        "The settled theme pill was placed in front of the original labels.");
                }
                Check(ReferenceEquals(headerIcon.Source, headerSource), "The header icon changed with the theme.");
                if (glassEnabled && theme != 0)
                {
                    SaveSnapshot(Path.ChangeExtension(report, theme == 1 ? ".progress-light.png" : ".progress-dark.png"));
                    var start = Required<Button>("StartPdfToolButton");
                    var presenter = start.GetVisualDescendants().OfType<ContentPresenter>()
                        .Single(item => item.Name == "PART_ContentPresenter");
                    var footer = Required<Grid>("ActionFooter");
                    Check(!footer.ClipToBounds && !start.ClipToBounds &&
                          footer.Parent is Panel footerParent &&
                          footerParent.Children.All(sibling => sibling == footer || sibling.ZIndex < footer.ZIndex),
                        "The start footer must draw in front of the other content.");
                    var faces = start.GetVisualDescendants().OfType<Control>()
                        .Where(item => item.Classes.Contains("start-glass-motion")).ToArray();
                    Check(faces.Length == 2, "Start button is missing its moving face or label.");
                    bool AtOffset(double offset) => faces.All(item =>
                        Math.Abs((item.RenderTransform?.Value.M32 ?? 0) - offset) < .1);
                    await Until(() => AtOffset(0));
                    var states = (IPseudoClasses)start.Classes;
                    states.Set(":pointerover", true);
                    try
                    {
                        await Until(() => AtOffset(-2));
                        Check(faces.All(face => face.TranslatePoint(default, start) is { Y: >= -0.1 }),
                            "The raised glass or label extends outside the button's top edge.");
                        CheckStartColors();
                        SaveSnapshot(Path.ChangeExtension(report, theme == 1 ? ".start-hover-light.png" : ".start-hover-dark.png"));
                        states.Set(":pressed", true);
                        await Until(() => AtOffset(1));
                        CheckStartColors();
                    }
                    finally
                    {
                        states.Set(":pressed", false);
                        states.Set(":pointerover", false);
                    }

                    await Until(() => AtOffset(0));
                    void CheckStartColors() =>
                        Check(presenter.Background is null && presenter.Foreground is ISolidColorBrush label &&
                              (theme == 1 ? label.Color.R > 220 : label.Color.R < 48),
                            "Start button lost the theme label or covered the glass face.");
                }
            }
            if (glassEnabled)
            {
                foreach (int theme in new[] { 1, 2 })
                {
                    FinishThemeDrag(theme);
                    await Until(() => !_themeTransitionActive && _themeMode == theme);
                    var themeName = theme == 1 ? "light" : "dark";
                    foreach (var (selectorIndex, mode) in new[]
                    {
                        (0, ConversionMode.PdfTools),
                        (1, ConversionMode.PdfTools),
                        (2, ConversionMode.PdfToImage),
                        (3, ConversionMode.PdfTools),
                        (4, ConversionMode.Ocr)
                    })
                    {
                        SetMode(mode);
                        var (pillName, _, labelsName, step) = GlassSelectors[selectorIndex];
                        var lens = _selectorLenses[pillName];
                        var pill = Required<Border>(pillName);
                        var labels = Required<Grid>(labelsName);
                        await Until(() => labels.IsEffectivelyVisible && lens.HasLabelBackdrop);
                        var translation = (TranslateTransform)pill.RenderTransform!;
                        var transitions = translation.Transitions;
                        var originalX = translation.X;
                        translation.Transitions = null;
                        try
                        {
                            translation.X = step / 2;
                            if (OperatingSystem.IsWindows())
                            {
                                await Until(() => lens.UsesWarpedLabelLayer && !lens.LabelsAreLive);
                                await Task.Delay(50);
                                Check(pill.ZIndex == labels.ZIndex,
                                    $"The moving pill obscures the underlying controls: {pillName}.");
                            }
                            else
                                Check(pill.ZIndex > labels.ZIndex, $"The moving pill is behind its labels: {pillName}.");
                            var straightPath = Path.ChangeExtension(report, $".{pillName}-straight-{themeName}.png");
                            var bentPath = Path.ChangeExtension(report, $".{pillName}-bent-{themeName}.png");
                            lens.SetLabelRefraction(false);
                            SaveSnapshot(straightPath);
                            lens.SetLabelRefraction(true);
                            SaveSnapshot(bentPath);

                            using var straight = SkiaSharp.SKBitmap.Decode(straightPath);
                            using var bent = SkiaSharp.SKBitmap.Decode(bentPath);
                            var labelStart = labels.TranslatePoint(default, this)!.Value;
                            var labelEnd = labels.TranslatePoint(new Point(labels.Bounds.Width, labels.Bounds.Height), this)!.Value;
                            var left = pill.TranslatePoint(default, this)!.Value.X;
                            var right = pill.TranslatePoint(new Point(pill.Bounds.Width, 0), this)!.Value.X;
                            var center = (left + right) / 2;
                            int leftChanges = 0, rightChanges = 0, outsideChanges = 0;
                            for (int y = (int)labelStart.Y; y < (int)labelEnd.Y; y++)
                            for (int x = (int)labelStart.X; x < (int)labelEnd.X; x++)
                            {
                                if (straight.GetPixel(x, y) == bent.GetPixel(x, y))
                                    continue;
                                if (x + 1 < left || x > right)
                                    outsideChanges++;
                                else if (x < center)
                                    leftChanges++;
                                else
                                    rightChanges++;
                            }

                            Check(leftChanges > 10 && rightChanges > 10 && outsideChanges == 0,
                                $"Label refraction on {pillName} in {themeName}: left={leftChanges}, right={rightChanges}, outside={outsideChanges}.");
                            for (int stop = 0; stop < labels.Children.Count; stop++)
                            {
                                translation.X = stop * step;
                                Check(pill.ZIndex == labels.ZIndex && !lens.UsesWarpedLabelLayer && lens.LabelsAreLive,
                                    $"The settled pill covers its original labels at stop {stop}: {pillName}.");
                            }
                            checks.Add($"{pillName} refracts labels at both edges in {themeName} theme");
                        }
                        finally
                        {
                            lens.SetLabelRefraction(true);
                            translation.X = originalX;
                            translation.Transitions = transitions;
                        }
                    }

                    foreach (var (name, mode) in new[]
                    {
                        ("PdfOutputSettings", ConversionMode.PdfToImage),
                        ("OcrSettings", ConversionMode.Ocr)
                    })
                    {
                        SetMode(mode);
                        var scroll = Required<ScrollViewer>(name + "Scroll");
                        var viewport = Required<Grid>(name + "Viewport");
                        var edge = viewport.Children.OfType<SettingsScrollGlass>().Single();
                        var rim = viewport.Children.OfType<SettingsScrollRim>().SingleOrDefault();
                        Check(OperatingSystem.IsWindows() == (rim is not null), $"Incorrect scroll rim for this platform: {name}.");
                        await Until(() => scroll.Viewport.Height > 0 && scroll.Extent.Height > scroll.Viewport.Height);
                        var presenter = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
                        var bar = scroll.GetVisualDescendants().OfType<ScrollBar>()
                            .Single(item => item.TemplatedParent == scroll && item.Orientation == Avalonia.Layout.Orientation.Vertical);
                        var thumb = bar.GetVisualDescendants().OfType<PanelScrollThumb>().Single();
                        var face = thumb.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "PanelScrollFace");
                        await Until(() => thumb.Opacity == 0);
                        Check(!thumb.IsHitTestVisible && !bar.GetVisualDescendants().OfType<RepeatButton>().Any(),
                            "The idle scrollbar intercepts input or has arrow buttons.");
                        scroll.Offset = new Vector(0, 24);
                        await Until(() => thumb.Opacity == 1);
                        Check(thumb.IsHitTestVisible && face.Width == 3, "The scrolling thumb did not appear at its normal width.");
                        var hover = (IPseudoClasses)thumb.Classes;
                        hover.Set(":pointerover", true);
                        try
                        {
                            Check(face.Width == 5, "The scrollbar hover size is incorrect.");
                            await Until(() => thumb.Opacity == 0 && !thumb.IsHitTestVisible);
                        }
                        finally
                        {
                            hover.Set(":pointerover", false);
                        }
                        checks.Add($"{name} shows a slim scrollbar while scrolling and hides it after idle even when hovered");
                        var panel = viewport.GetVisualAncestors().OfType<Border>()
                            .First(border => border.Classes.Contains("settings-panel"));
                        var panelStart = presenter.TranslatePoint(default, panel)!.Value;
                        var panelEnd = presenter.TranslatePoint(new Point(presenter.Bounds.Width, presenter.Bounds.Height), panel)!.Value;
                        double topInset = panelStart.Y;
                        double bottomInset = panel.Bounds.Height - panelEnd.Y;
                        Check(panel.ClipToBounds && topInset is >= 2 and < 4 && bottomInset is >= 2 and < 4,
                            "The settings viewport does not keep the expected thin rim: " + name);
                        var max = scroll.Extent.Height - scroll.Viewport.Height;
                        if (rim is not null)
                        {
                            Check(!rim.IsHitTestVisible && !rim.Focusable, "Scroll rim intercepts input: " + name);
                            scroll.Offset = new Vector(0, max / 2);
                            await Until(() => rim.TopStrength > 0 && rim.BottomStrength > 0);
                            scroll.Offset = new Vector(0, max);
                            await Until(() => rim.TopStrength > 0 && rim.BottomStrength == 0);
                            scroll.Offset = default;
                            await Until(() => rim.TopStrength == 0 && rim.BottomStrength > 0);
                            checks.Add($"{name} shows rim reflections in {themeName} theme");
                        }

                        var captions = presenter.GetVisualDescendants().OfType<TextBlock>()
                            .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text) &&
                                           text.Bounds.Height is > 8 and < 60)
                            .Select(text => (Y: text.TranslatePoint(default, presenter)!.Value.Y + scroll.Offset.Y,
                                             Height: text.Bounds.Height))
                            .ToArray();
                        foreach (var (isTop, atEnd) in new[] { (true, false), (false, false), (true, true), (false, true) })
                        {
                            var offset = atEnd ? (isTop ? 0 : max) : captions.Select(caption => isTop
                                    ? caption.Y + caption.Height * .35
                                    : caption.Y + caption.Height * .72 - presenter.Bounds.Height)
                                .First(candidate => candidate > SettingsScrollGlass.BandDepth &&
                                                    candidate < max - SettingsScrollGlass.BandDepth);
                            scroll.Offset = new Vector(0, offset);
                            if (OperatingSystem.IsWindows())
                                Check(!edge.HasBackdrop, "Scroll edge captured content before scrolling stopped: " + name);
                            await Until(() => edge.HasBackdrop);
                            // The edge redraw must still be in place after the scroll input has stopped.
                            await Task.Delay(350);
                            if (atEnd && scroll.Content is StackPanel content)
                            {
                                var item = isTop ? content.Children.First() : content.Children.Last();
                                var itemEdge = item.TranslatePoint(new Point(0, isTop ? 0 : item.Bounds.Height), presenter)!.Value.Y;
                                var padding = isTop ? itemEdge : presenter.Bounds.Height - itemEdge;
                                Check(padding >= 11, "The first or last settings item lost its scrollable spacing: " + name);
                            }
                            var endName = (isTop ? "top" : "bottom") + (atEnd ? "-limit" : "");
                            var normal = Path.ChangeExtension(report, $".{name}-{endName}-straight-{themeName}.png");
                            var refracted = Path.ChangeExtension(report, $".{name}-{endName}-bent-{themeName}.png");
                            edge.Opacity = 0;
                            try
                            {
                                SaveSnapshot(normal);
                            }
                            finally
                            {
                                edge.Opacity = 1;
                            }
                            SaveSnapshot(refracted);

                            var start = presenter.TranslatePoint(default, this)!.Value;
                            var end = presenter.TranslatePoint(new Point(presenter.Bounds.Width, presenter.Bounds.Height), this)!.Value;
                            var band = presenter.TranslatePoint(new Point(0, SettingsScrollGlass.BandDepth), this)!.Value.Y - start.Y;
                            using var plain = SkiaSharp.SKBitmap.Decode(normal);
                            using var bent = SkiaSharp.SKBitmap.Decode(refracted);
                            int ChangedPixels(int from, int to)
                            {
                                int changes = 0;
                                for (int y = from; y < to; y++)
                                for (int x = (int)start.X + 3; x < (int)end.X - 16; x++)
                                {
                                    var a = plain.GetPixel(x, y);
                                    var b = bent.GetPixel(x, y);
                                    if (Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue) > 18)
                                        changes++;
                                }
                                return changes;
                            }

                            int changed = isTop ? ChangedPixels((int)start.Y + 1, (int)(start.Y + band))
                                : ChangedPixels((int)(end.Y - band), (int)end.Y - 1);
                            int middle = ChangedPixels((int)(start.Y + band) + 1, (int)(end.Y - band) - 1);
                            Check((atEnd ? changed == 0 : changed > 20) && middle == 0,
                                $"Stopped scroll glass on {name} {endName} in {themeName}: edge={changed}, middle={middle}.");
                        }
                        Check(!edge.IsHitTestVisible && !edge.Focusable, "Scroll effect intercepts input: " + name);
                        checks.Add($"{name} retains both refracted edges after scrolling stops and clears them at the limits in {themeName} theme");
                        scroll.Offset = default;
                    }
                }
                SetMode(ConversionMode.PdfTools);
            }
            _mainProgressBar.Value = 0;
            if (glassEnabled)
                checks.Add("start button motion and label contrast on hover, press and return in both themes");
            checks.Add("real desktop startup and the same header icon in Auto, Light and Dark");
            Program.TraceGuiVerification("Theme verification finished");
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
            await VerifyV51Async(root, report, checks, Until);
            Program.TraceGuiVerification("v5.1 controls and conversions verified");
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
            var dpiTrack = Required<Border>("PdfDpiTrack");
            var dpiTrackGlass = (dpiTrack.Child as Panel)?.Children.OfType<ButtonGlass>().SingleOrDefault();
            var dpiRangeGlass = _pdfDpiRange.Child as ButtonGlass;
            var dpiThumbGlass = _pdfDpiThumb.Child as ButtonGlass;
            Check(new[] { dpiTrackGlass, dpiRangeGlass, dpiThumbGlass }.All(glass => (glass is not null) == glassEnabled),
                "Incorrect glass decoration on the PDF resolution slider.");
            for (var index = 0; index < PdfDpiValues.Length; index++)
            {
                SetPdfDpiIndex(index);
                var target = PdfDpiThumbPositions[index];
                var width = PdfDpiRangeWidths[index];
                await Until(() => Math.Abs(_pdfDpiThumbTransform.X - target) < 1 &&
                    Math.Abs(_pdfDpiRange.Bounds.Width - width) < 2);
                Check(GetPdfDpi() == PdfDpiValues[index] && _pdfDpiCurrentHint.Text ==
                    $"{PdfDpiLabels[index]} · {PdfDpiValues[index]} dpi" && _pdfDpiTicks[index].Classes.Contains("selected"),
                    "Incorrect resolution selection or label.");
            }
            if (glassEnabled)
            {
                await Until(() => dpiTrackGlass!.HasRendered && dpiRangeGlass!.HasRendered && dpiThumbGlass!.HasRendered);
                SetPdfDpiIndex(1);
                await Until(() => Math.Abs(_pdfDpiRange.Bounds.Width - PdfDpiRangeWidths[1]) < 2);
                SaveSnapshot(Path.ChangeExtension(report, ".dpi-dark.png"));
                FinishThemeDrag(1);
                await Until(() => !_themeTransitionActive);
                SaveSnapshot(Path.ChangeExtension(report, ".dpi-light.png"));
                FinishThemeDrag(2);
                await Until(() => !_themeTransitionActive);
            }
            SetPdfDpiIndex(0);
            checks.Add("PDF resolution slider glass and the 200, 300 and 600 DPI selections");
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
            var done = editor.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "完了");
            await Until(() => done.Bounds.Width > 0);
            Check(ButtonGlass.IsAppliedTo(done) == glassEnabled,
                "Incorrect glass decoration on the editor Done button.");
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
            done.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
            Button? thumbnail = null;
            await Until(() => (thumbnail = this.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.IsEffectivelyVisible && button.CornerRadius.TopLeft == 12 && button.Bounds.Height > 26)) is not null);
            Check(ButtonGlass.IsAppliedTo(thumbnail!) == glassEnabled,
                "Incorrect glass decoration on the rounded-rectangle OCR preview button.");
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
            SaveSnapshot(Path.ChangeExtension(report, ".png"));
            if (Program.GlassTrial == ModePillGlassTrial.On)
                Check(_selectorLenses.Count == GlassSelectors.Length && _selectorLenses.Values.All(lens => lens.IsAvailable && lens.HasRendered),
                    "A selector stopped drawing its glass during GUI verification.");
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
