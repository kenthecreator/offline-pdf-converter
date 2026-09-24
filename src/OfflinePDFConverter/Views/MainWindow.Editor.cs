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

public partial class MainWindow
{
    private async Task OpenSimpleEditWindowAsync(PdfPagePreviewItem pageItem)
    {
        const double baseCanvasWidth = 720;
        double previewZoom = 1.0;
        var canvasWidth = baseCanvasWidth;
        var canvasHeight = Math.Max(420, canvasWidth * pageItem.PageHeightPoints / pageItem.PageWidthPoints);
        var pageEdits = new ObservableCollection<PdfTextEditDraft>(
            _pdfTextEdits.Where(edit => edit.PageNumber == pageItem.PageNumber));
        var pageShapes = new ObservableCollection<PdfShapeEditDraft>(
            _pdfShapeEdits.Where(shape => shape.PageNumber == pageItem.PageNumber));
        PdfTextEditDraft? selectedEdit = null;
        PdfTextEditDraft? activeEdit = null;
        PdfShapeEditDraft? selectedShape = null;
        PdfShapeEditDraft? activeShape = null;
        object? copiedDraft = null;
        var undoStack = new Stack<(List<PdfTextEditDraft> Texts, List<PdfShapeEditDraft> Shapes)>();
        bool isRestoringUndo = false;
        bool isResizing = false;
        bool isAdjustingCornerRadius = false;
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
        double startCornerRadius = 0;
        double startRotationDegrees = 0;
        var dialogBackground = _isDarkTheme
            ? new SolidColorBrush(Color.Parse("#191919"))
            : new SolidColorBrush(Color.Parse("#F2F2F2"));
        var panelBackground = _isDarkTheme
            ? new SolidColorBrush(Color.Parse("#222222"))
            : new SolidColorBrush(Color.Parse("#FAFAFA"));
        var dialogTextBrush = _isDarkTheme
            ? new SolidColorBrush(Color.Parse("#F2F2F2"))
            : new SolidColorBrush(Color.Parse("#202020"));
        var dialogMutedBrush = _isDarkTheme
            ? new SolidColorBrush(Color.Parse("#B8B8B8"))
            : new SolidColorBrush(Color.Parse("#606060"));
        IBrush dialogFieldBrush = _isDarkTheme
            ? new SolidColorBrush(Color.Parse("#2D2D2D"))
            : Brushes.White;
        var dialogFieldBorderBrush = _isDarkTheme
            ? new SolidColorBrush(Color.Parse("#606060"))
            : new SolidColorBrush(Color.Parse("#BEBEBE"));
        var actionBackgroundBrush = _isDarkTheme
            ? new SolidColorBrush(Color.Parse("#E5E5E5"))
            : new SolidColorBrush(Color.Parse("#2C2C2C"));
        IBrush actionForegroundBrush = _isDarkTheme
            ? new SolidColorBrush(Color.Parse("#121212"))
            : Brushes.White;
        var actionBorderBrush = _isDarkTheme
            ? new SolidColorBrush(Color.Parse("#F5F5F5"))
            : new SolidColorBrush(Color.Parse("#5C5C5C"));

        var canvas = new Canvas
        {
            Width = canvasWidth,
            Height = canvasHeight,
            Background = Brushes.White,
            Focusable = true
        };
        var previewImage = new Image
        {
            Source = pageItem.Thumbnail,
            Width = canvasWidth,
            Height = canvasHeight,
            Stretch = Stretch.Fill
        };
        canvas.Children.Add(previewImage);

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
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.Parse("#999999")),
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
        var boldToggle = new Avalonia.Controls.Primitives.ToggleButton
        {
            Content = "B",
            FontWeight = FontWeight.Bold,
            MinWidth = 54,
            MinHeight = 38,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = dialogFieldBrush,
            Foreground = dialogTextBrush,
            BorderBrush = dialogFieldBorderBrush
        };
        var underlineToggle = new Avalonia.Controls.Primitives.ToggleButton
        {
            Content = new TextBlock
            {
                Text = "U",
                TextDecorations = TextDecorations.Underline,
                FontWeight = FontWeight.Bold
            },
            MinWidth = 54,
            MinHeight = 38,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = dialogFieldBrush,
            Foreground = dialogTextBrush,
            BorderBrush = dialogFieldBorderBrush
        };
        var widthBox = CreateEditTextBox("160");
        var heightBox = CreateEditTextBox("32");
        var backgroundColorBox = CreateEditTextBox("None");
        var backgroundColorSwatch = new Border
        {
            Width = 42,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.Parse("#999999")),
            BorderThickness = new Thickness(1)
        };
        var shapeFillColorBox = CreateEditTextBox("None");
        var shapeStrokeColorBox = CreateEditTextBox("#000000");
        var shapeStrokeThicknessBox = CreateEditTextBox("2");
        var shapeFillSwatch = CreateColorSwatch(42, 32, Brushes.Transparent);
        var shapeStrokeSwatch = CreateColorSwatch(42, 32, Brushes.Black);
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
        var chooseShapeFillColorButton = new Button
        {
            Content = "色を選択",
            Classes = { "small-action" }
        };
        var chooseShapeStrokeColorButton = new Button
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
                        Background = actionForegroundBrush,
                        Child = new TextBlock
                        {
                            Text = "+",
                            FontSize = 29,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = actionBackgroundBrush,
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
                        Foreground = actionForegroundBrush,
                        TextTrimming = TextTrimming.None,
                        VerticalAlignment = VerticalAlignment.Center,
                        [Grid.ColumnProperty] = 1
                    }
                }
            },
            MinHeight = 52,
            Padding = new Thickness(12, 8),
            CornerRadius = new CornerRadius(999),
            Background = actionBackgroundBrush,
            Foreground = actionForegroundBrush,
            BorderBrush = actionBorderBrush,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch
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

        Border CreateColorSwatch(double width, double height, IBrush fallback)
        {
            return new Border
            {
                Width = width,
                Height = height,
                CornerRadius = new CornerRadius(8),
                Background = fallback,
                BorderBrush = new SolidColorBrush(Color.Parse("#999999")),
                BorderThickness = new Thickness(1)
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
            backgroundColorSwatch.Background = IsNoColor(backgroundColorBox.Text)
                ? Brushes.Transparent
                : TryParseColor(backgroundColorBox.Text, out var color)
                    ? new SolidColorBrush(color)
                    : Brushes.Transparent;
            textColorSwatch.Background = IsNoColor(textColorBox.Text)
                ? Brushes.Transparent
                : TryParseColor(textColorBox.Text, out var textColor)
                    ? new SolidColorBrush(textColor)
                    : Brushes.Transparent;
            shapeFillSwatch.Background = IsNoColor(shapeFillColorBox.Text)
                ? Brushes.Transparent
                : TryParseColor(shapeFillColorBox.Text, out var shapeFillColor)
                    ? new SolidColorBrush(shapeFillColor)
                    : Brushes.Transparent;
            shapeStrokeSwatch.Background = IsNoColor(shapeStrokeColorBox.Text)
                ? Brushes.Transparent
                : TryParseColor(shapeStrokeColorBox.Text, out var shapeStrokeColor)
                    ? new SolidColorBrush(shapeStrokeColor)
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
                boldToggle.IsChecked = edit.IsBold;
                underlineToggle.IsChecked = edit.IsUnderline;
                UpdateSwatch();
            }
            finally
            {
                isLoadingSelection = false;
            }
        }

        void LoadSelectedShape(PdfShapeEditDraft? shape)
        {
            isLoadingSelection = true;
            try
            {
                if (shape == null)
                {
                    return;
                }

                widthBox.Text = shape.Width.ToString("0.#");
                heightBox.Text = shape.Height.ToString("0.#");
                shapeFillColorBox.Text = shape.FillColorHex;
                shapeStrokeColorBox.Text = shape.StrokeColorHex;
                shapeStrokeThicknessBox.Text = shape.StrokeThickness.ToString("0.#");
                UpdateSwatch();
            }
            finally
            {
                isLoadingSelection = false;
            }
        }

        void ApplyFieldsToSelected()
        {
            if (isLoadingSelection || isRestoringUndo || (selectedEdit == null && selectedShape == null))
            {
                return;
            }

            PushUndo();
            if (selectedEdit != null)
            {
                selectedEdit.Text = textBox.Text?.Trim() ?? string.Empty;
                selectedEdit.FontFamily = SelectedFontFamily();
                selectedEdit.IsBold = boldToggle.IsChecked == true;
                selectedEdit.IsUnderline = underlineToggle.IsChecked == true;
            }
            if (double.TryParse(fontSizeBox.Text, out var fontSize) && fontSize > 0)
            {
                if (selectedEdit != null)
                {
                    selectedEdit.FontSize = fontSize;
                }
            }

            if (double.TryParse(widthBox.Text, out var width) && width > 0)
            {
                if (selectedEdit != null)
                {
                    selectedEdit.Width = width;
                }
                else if (selectedShape != null)
                {
                    selectedShape.Width = width;
                }
            }

            if (double.TryParse(heightBox.Text, out var height) && height > 0)
            {
                if (selectedEdit != null)
                {
                    selectedEdit.Height = height;
                }
                else if (selectedShape != null)
                {
                    selectedShape.Height = height;
                }
            }

            if (selectedEdit != null)
            {
                selectedEdit.BackgroundColorHex = NormalizeOptionalColorHex(backgroundColorBox.Text);
                selectedEdit.TextColorHex = NormalizeOptionalColorHex(textColorBox.Text);
                selectedEdit.AddWhiteBox = !IsNoColor(backgroundColorBox.Text);
            }
            else if (selectedShape != null)
            {
                selectedShape.FillColorHex = NormalizeOptionalColorHex(shapeFillColorBox.Text);
                selectedShape.StrokeColorHex = NormalizeOptionalColorHex(shapeStrokeColorBox.Text);
                if (double.TryParse(shapeStrokeThicknessBox.Text, out var strokeThickness) && strokeThickness >= 0)
                {
                    selectedShape.StrokeThickness = strokeThickness;
                }
            }

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

            PushUndo();
            pageEdits.Remove(target);
            _pdfTextEdits.Remove(target);
            selectedEdit = null;
            inlineEditingEdit = null;
            textBox.Text = string.Empty;
            RefreshOverlays();
        }

        void DeleteSelectedShape(PdfShapeEditDraft? shapeToDelete = null)
        {
            var target = shapeToDelete ?? selectedShape;
            if (target == null)
            {
                return;
            }

            PushUndo();
            pageShapes.Remove(target);
            _pdfShapeEdits.Remove(target);
            selectedShape = null;
            RefreshOverlays();
        }

        textBox.TextChanged += (_, _) => ApplyFieldsToSelected();
        fontFamilyCombo.SelectionChanged += (_, _) => ApplyFieldsToSelected();
        fontSizeBox.TextChanged += (_, _) => ApplyFieldsToSelected();
        widthBox.TextChanged += (_, _) => ApplyFieldsToSelected();
        heightBox.TextChanged += (_, _) => ApplyFieldsToSelected();
        backgroundColorBox.TextChanged += (_, _) => ApplyFieldsToSelected();
        textColorBox.TextChanged += (_, _) => ApplyFieldsToSelected();
        shapeFillColorBox.TextChanged += (_, _) => ApplyFieldsToSelected();
        shapeStrokeColorBox.TextChanged += (_, _) => ApplyFieldsToSelected();
        shapeStrokeThicknessBox.TextChanged += (_, _) => ApplyFieldsToSelected();
        boldToggle.IsCheckedChanged += (_, _) => ApplyFieldsToSelected();
        underlineToggle.IsCheckedChanged += (_, _) => ApplyFieldsToSelected();
        bool HandleTextStyleShortcut(KeyEventArgs e)
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                return false;
            }

            if (e.Key == Key.B)
            {
                boldToggle.IsChecked = boldToggle.IsChecked != true;
                e.Handled = true;
                return true;
            }

            if (e.Key == Key.U)
            {
                underlineToggle.IsChecked = underlineToggle.IsChecked != true;
                e.Handled = true;
                return true;
            }

            return false;
        }

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
                AddWhiteBox = !IsNoColor(backgroundColorBox.Text),
                BackgroundColorHex = NormalizeOptionalColorHex(backgroundColorBox.Text),
                TextColorHex = NormalizeOptionalColorHex(textColorBox.Text),
                TextAlignment = "Left",
                IsBold = boldToggle.IsChecked == true,
                IsUnderline = underlineToggle.IsChecked == true
            };
        }

        void AddTextBoxAt(double pdfX, double pdfY)
        {
            PushUndo();
            var edit = CreateTextEdit(pdfX, pdfY);
            edit.X = Math.Clamp(edit.X, 0, Math.Max(0, pageItem.PageWidthPoints - edit.Width));
            edit.Y = Math.Clamp(edit.Y, 0, Math.Max(0, pageItem.PageHeightPoints - edit.Height));
            pageEdits.Add(edit);
            selectedEdit = edit;
            selectedShape = null;
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

        PdfShapeEditDraft CreateShapeEdit(string shapeType, double pdfX, double pdfY)
        {
            var defaultWidth = shapeType == "Line" ? 160 : 120;
            var defaultHeight = shapeType == "Line" ? 0 : 120;
            return new PdfShapeEditDraft
            {
                PageNumber = pageItem.PageNumber,
                X = Math.Round(Math.Clamp(pdfX, 0, pageItem.PageWidthPoints), 1),
                Y = Math.Round(Math.Clamp(pdfY, 0, pageItem.PageHeightPoints), 1),
                Width = defaultWidth,
                Height = defaultHeight,
                ShapeType = shapeType,
                FillColorHex = NormalizeOptionalColorHex(shapeFillColorBox.Text),
                StrokeColorHex = NormalizeOptionalColorHex(shapeStrokeColorBox.Text),
                StrokeThickness = ParseNonNegativeDouble(shapeStrokeThicknessBox.Text, "境界線の太さ"),
                CornerRadius = shapeType == "RoundedRectangle" ? 24 : 0,
                RotationDegrees = 0
            };
        }

        void AddShapeAt(string shapeType)
        {
            PushUndo();
            var defaultWidth = shapeType == "Line" ? 160 : 120;
            var defaultHeight = shapeType == "Line" ? 0 : 120;
            var shape = CreateShapeEdit(
                shapeType,
                (pageItem.PageWidthPoints - defaultWidth) / 2,
                (pageItem.PageHeightPoints - defaultHeight) / 2);
            if (IsLineShape(shape))
            {
                shape.X = Math.Clamp(shape.X, 0, pageItem.PageWidthPoints);
                shape.Y = Math.Clamp(shape.Y, 0, pageItem.PageHeightPoints);
                shape.Width = Math.Clamp(shape.Width, -shape.X, pageItem.PageWidthPoints - shape.X);
                shape.Height = Math.Clamp(shape.Height, -shape.Y, pageItem.PageHeightPoints - shape.Y);
            }
            else
            {
                shape.X = Math.Clamp(shape.X, 0, Math.Max(0, pageItem.PageWidthPoints - shape.Width));
                shape.Y = Math.Clamp(shape.Y, 0, Math.Max(0, pageItem.PageHeightPoints - shape.Height));
            }
            pageShapes.Add(shape);
            selectedEdit = null;
            selectedShape = shape;
            LoadSelectedShape(shape);
            RefreshOverlays();
            canvas.Focus();
        }

        Button ShapeButton(string text, string shapeType)
        {
            Control icon = shapeType switch
            {
                "Ellipse" => new Avalonia.Controls.Shapes.Ellipse
                {
                    Width = 34,
                    Height = 34,
                    Stroke = Brushes.Black,
                    StrokeThickness = 5,
                    Fill = Brushes.Transparent,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                "RoundedRectangle" => new Avalonia.Controls.Shapes.Rectangle
                {
                    Width = 38,
                    Height = 30,
                    RadiusX = 11,
                    RadiusY = 11,
                    Stroke = Brushes.Black,
                    StrokeThickness = 5,
                    Fill = Brushes.Transparent,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                "Line" => new Avalonia.Controls.Shapes.Line
                {
                    StartPoint = new Point(11, 37),
                    EndPoint = new Point(41, 7),
                    Stroke = Brushes.Black,
                    StrokeThickness = 6
                },
                _ => new Avalonia.Controls.Shapes.Rectangle
                {
                    Width = 34,
                    Height = 34,
                    Stroke = Brushes.Black,
                    StrokeThickness = 5,
                    Fill = Brushes.Transparent,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            var button = new Button
            {
                Content = new Border
                {
                    Width = 52,
                    Height = 52,
                    CornerRadius = new CornerRadius(12),
                    Background = new SolidColorBrush(Color.Parse("#E5E5E5")),
                    Child = new Grid
                    {
                        Children = { icon }
                    }
                },
                Classes = { "small-action" },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Padding = new Thickness(0),
                MinHeight = 58
            };
            button.Classes.Remove("small-action");
            ToolTip.SetTip(button, text);
            button.Click += (_, _) => AddShapeAt(shapeType);
            return button;
        }

        PdfTextEditDraft CloneTextEdit(PdfTextEditDraft source, double offset = 0)
        {
            return new PdfTextEditDraft
            {
                PageNumber = pageItem.PageNumber,
                X = Math.Clamp(source.X + offset, 0, Math.Max(0, pageItem.PageWidthPoints - source.Width)),
                Y = Math.Clamp(source.Y + offset, 0, Math.Max(0, pageItem.PageHeightPoints - source.Height)),
                Width = source.Width,
                Height = source.Height,
                Text = source.Text,
                FontFamily = source.FontFamily,
                FontSize = source.FontSize,
                AddWhiteBox = source.AddWhiteBox,
                BackgroundColorHex = source.BackgroundColorHex,
                TextColorHex = source.TextColorHex,
                TextAlignment = source.TextAlignment,
                IsBold = source.IsBold,
                IsUnderline = source.IsUnderline
            };
        }

        PdfShapeEditDraft CloneShapeEdit(PdfShapeEditDraft source, double offset = 0)
        {
            if (IsLineShape(source))
            {
                var minX = Math.Min(source.X, source.X + source.Width);
                var maxX = Math.Max(source.X, source.X + source.Width);
                var minY = Math.Min(source.Y, source.Y + source.Height);
                var maxY = Math.Max(source.Y, source.Y + source.Height);
                var clampedOffsetX = Math.Clamp(offset, -minX, pageItem.PageWidthPoints - maxX);
                var clampedOffsetY = Math.Clamp(offset, -minY, pageItem.PageHeightPoints - maxY);
                return new PdfShapeEditDraft
                {
                    PageNumber = pageItem.PageNumber,
                    X = Math.Round(source.X + clampedOffsetX, 1),
                    Y = Math.Round(source.Y + clampedOffsetY, 1),
                    Width = source.Width,
                    Height = source.Height,
                    ShapeType = "Line",
                    FillColorHex = source.FillColorHex,
                    StrokeColorHex = source.StrokeColorHex,
                    StrokeThickness = source.StrokeThickness,
                    CornerRadius = source.CornerRadius,
                    RotationDegrees = 0
                };
            }

            return new PdfShapeEditDraft
            {
                PageNumber = pageItem.PageNumber,
                X = Math.Clamp(source.X + offset, 0, Math.Max(0, pageItem.PageWidthPoints - source.Width)),
                Y = Math.Clamp(source.Y + offset, 0, Math.Max(0, pageItem.PageHeightPoints - source.Height)),
                Width = source.Width,
                Height = source.Height,
                ShapeType = source.ShapeType,
                FillColorHex = source.FillColorHex,
                StrokeColorHex = source.StrokeColorHex,
                StrokeThickness = source.StrokeThickness,
                CornerRadius = source.CornerRadius,
                RotationDegrees = source.RotationDegrees
            };
        }

        PdfTextEditDraft SnapshotTextEdit(PdfTextEditDraft source)
        {
            return new PdfTextEditDraft
            {
                PageNumber = source.PageNumber,
                X = source.X,
                Y = source.Y,
                Width = source.Width,
                Height = source.Height,
                Text = source.Text,
                FontFamily = source.FontFamily,
                FontSize = source.FontSize,
                AddWhiteBox = source.AddWhiteBox,
                BackgroundColorHex = source.BackgroundColorHex,
                TextColorHex = source.TextColorHex,
                TextAlignment = source.TextAlignment,
                IsBold = source.IsBold,
                IsUnderline = source.IsUnderline
            };
        }

        PdfShapeEditDraft SnapshotShapeEdit(PdfShapeEditDraft source)
        {
            return new PdfShapeEditDraft
            {
                PageNumber = source.PageNumber,
                X = source.X,
                Y = source.Y,
                Width = source.Width,
                Height = source.Height,
                ShapeType = IsLineShape(source) ? "Line" : source.ShapeType,
                FillColorHex = source.FillColorHex,
                StrokeColorHex = source.StrokeColorHex,
                StrokeThickness = source.StrokeThickness,
                CornerRadius = source.CornerRadius,
                RotationDegrees = source.RotationDegrees
            };
        }

        void PushUndo()
        {
            if (isLoadingSelection || isRestoringUndo)
            {
                return;
            }

            undoStack.Push((
                pageEdits.Select(SnapshotTextEdit).ToList(),
                pageShapes.Select(SnapshotShapeEdit).ToList()));
        }

        void RestoreUndo()
        {
            if (undoStack.Count == 0)
            {
                return;
            }

            var snapshot = undoStack.Pop();
            isRestoringUndo = true;
            try
            {
                activeEdit = null;
                activeShape = null;
                selectedEdit = null;
                selectedShape = null;
                inlineEditingEdit = null;
                pageEdits.Clear();
                foreach (var edit in snapshot.Texts.Select(SnapshotTextEdit))
                {
                    pageEdits.Add(edit);
                }

                pageShapes.Clear();
                foreach (var shape in snapshot.Shapes.Select(SnapshotShapeEdit))
                {
                    pageShapes.Add(shape);
                }

                isLoadingSelection = true;
                try
                {
                    textBox.Text = string.Empty;
                    widthBox.Text = "160";
                    heightBox.Text = "32";
                }
                finally
                {
                    isLoadingSelection = false;
                }
            }
            finally
            {
                isRestoringUndo = false;
            }

            UpdateSwatch();
            RefreshOverlays();
            canvas.Focus();
        }

        void ResizeCanvas()
        {
            canvasWidth = baseCanvasWidth * previewZoom;
            canvasHeight = Math.Max(420 * previewZoom, canvasWidth * pageItem.PageHeightPoints / pageItem.PageWidthPoints);
            canvas.Width = canvasWidth;
            canvas.Height = canvasHeight;
            previewImage.Width = canvasWidth;
            previewImage.Height = canvasHeight;
            RefreshOverlays();
        }

        void ChangeZoom(double delta)
        {
            previewZoom = Math.Clamp(previewZoom + delta, 0.5, 3.0);
            ResizeCanvas();
        }

        static double ClampCornerRadius(PdfShapeEditDraft shape)
        {
            return Math.Clamp(shape.CornerRadius, 0, Math.Max(0, Math.Min(Math.Abs(shape.Width), Math.Abs(shape.Height)) / 2));
        }

        Point SnapPointTo45Degrees(double anchorX, double anchorY, double pointerX, double pointerY)
        {
            var deltaX = pointerX - anchorX;
            var deltaY = pointerY - anchorY;
            var length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            if (length < 0.1)
            {
                return new Point(anchorX, anchorY);
            }

            var snappedAngle = Math.Round(Math.Atan2(deltaY, deltaX) / (Math.PI / 4)) * (Math.PI / 4);
            return new Point(
                Math.Clamp(anchorX + Math.Cos(snappedAngle) * length, 0, pageItem.PageWidthPoints),
                Math.Clamp(anchorY + Math.Sin(snappedAngle) * length, 0, pageItem.PageHeightPoints));
        }

        static double NormalizeAngle(double degrees)
        {
            var normalized = degrees % 360;
            return normalized < 0 ? normalized + 360 : normalized;
        }

        double RotationFromPointer(double centerX, double centerY, double pointerX, double pointerY)
        {
            return NormalizeAngle(Math.Atan2(pointerY - centerY, pointerX - centerX) * 180 / Math.PI + 90);
        }

        void KeepShapeAspectRatio(ref double newX, ref double newY, ref double newWidth, ref double newHeight)
        {
            if (startWidth <= 0 || startHeight <= 0)
            {
                return;
            }

            var ratio = startWidth / startHeight;
            var changedWidth = Math.Abs(newWidth - startWidth);
            var changedHeight = Math.Abs(newHeight - startHeight);
            if (changedWidth >= changedHeight * ratio)
            {
                newHeight = Math.Max(8, newWidth / ratio);
            }
            else
            {
                newWidth = Math.Max(8, newHeight * ratio);
            }

            if (resizeHandlePosition.Contains("Left", StringComparison.Ordinal))
            {
                newX = startX + startWidth - newWidth;
            }
            else
            {
                newX = startX;
            }

            if (resizeHandlePosition.Contains("Top", StringComparison.Ordinal))
            {
                newY = startY + startHeight - newHeight;
            }
            else
            {
                newY = startY;
            }

            if (newX < 0)
            {
                newWidth += newX;
                newHeight = newWidth / ratio;
                newX = 0;
                if (resizeHandlePosition.Contains("Top", StringComparison.Ordinal))
                {
                    newY = startY + startHeight - newHeight;
                }
            }

            if (newY < 0)
            {
                newHeight += newY;
                newWidth = newHeight * ratio;
                newY = 0;
                if (resizeHandlePosition.Contains("Left", StringComparison.Ordinal))
                {
                    newX = startX + startWidth - newWidth;
                }
            }

            if (newX + newWidth > pageItem.PageWidthPoints)
            {
                newWidth = pageItem.PageWidthPoints - newX;
                newHeight = newWidth / ratio;
                if (resizeHandlePosition.Contains("Top", StringComparison.Ordinal))
                {
                    newY = startY + startHeight - newHeight;
                }
            }

            if (newY + newHeight > pageItem.PageHeightPoints)
            {
                newHeight = pageItem.PageHeightPoints - newY;
                newWidth = newHeight * ratio;
                if (resizeHandlePosition.Contains("Left", StringComparison.Ordinal))
                {
                    newX = startX + startWidth - newWidth;
                }
            }

            newX = Math.Clamp(newX, 0, pageItem.PageWidthPoints - 8);
            newY = Math.Clamp(newY, 0, pageItem.PageHeightPoints - 8);
            newWidth = Math.Clamp(newWidth, 8, pageItem.PageWidthPoints - newX);
            newHeight = Math.Clamp(newHeight, 8, pageItem.PageHeightPoints - newY);
        }

        void RefreshOverlays()
        {
            while (canvas.Children.Count > 1)
            {
                canvas.Children.RemoveAt(1);
            }

            foreach (var shape in pageShapes)
            {
                if (shape.ShapeType == "RoundedRectangle")
                {
                    shape.CornerRadius = ClampCornerRadius(shape);
                }

                var left = shape.X / pageItem.PageWidthPoints * canvasWidth;
                var top = shape.Y / pageItem.PageHeightPoints * canvasHeight;
                var width = shape.Width / pageItem.PageWidthPoints * canvasWidth;
                var height = shape.Height / pageItem.PageHeightPoints * canvasHeight;
                var strokeThickness = Math.Max(0, shape.StrokeThickness * canvasWidth / pageItem.PageWidthPoints);
                IBrush? fill = IsNoColor(shape.FillColorHex)
                    ? null
                    : TryParseColor(shape.FillColorHex, out var fillColor)
                        ? new SolidColorBrush(fillColor)
                        : null;
                IBrush? stroke = IsNoColor(shape.StrokeColorHex)
                    ? null
                    : TryParseColor(shape.StrokeColorHex, out var strokeColor)
                        ? new SolidColorBrush(strokeColor)
                        : Brushes.Black;

                if (IsLineShape(shape))
                {
                    var startPoint = new Point(left, top);
                    var endPoint = new Point(
                        (shape.X + shape.Width) / pageItem.PageWidthPoints * canvasWidth,
                        (shape.Y + shape.Height) / pageItem.PageHeightPoints * canvasHeight);
                    var lineStrokeThickness = Math.Max(1, strokeThickness);
                    var lineCanvas = new Canvas
                    {
                        Width = canvasWidth,
                        Height = canvasHeight
                    };
                    var line = new Avalonia.Controls.Shapes.Line
                    {
                        StartPoint = startPoint,
                        EndPoint = endPoint,
                        Stroke = stroke,
                        StrokeThickness = lineStrokeThickness
                    };
                    var hitLine = new Avalonia.Controls.Shapes.Line
                    {
                        StartPoint = startPoint,
                        EndPoint = endPoint,
                        Stroke = Brushes.Transparent,
                        StrokeThickness = Math.Max(18, lineStrokeThickness + 12)
                    };

                    Border LineHandle(string position, Point point)
                    {
                        var handle = new Border
                        {
                            Width = 9,
                            Height = 9,
                            Background = Brushes.White,
                            BorderBrush = new SolidColorBrush(Color.Parse("#181818")),
                            BorderThickness = new Thickness(1),
                            Cursor = new Cursor(StandardCursorType.SizeAll),
                            IsVisible = selectedShape == shape
                        };
                        handle.PointerPressed += (_, e) =>
                        {
                            PushUndo();
                            selectedEdit = null;
                            selectedShape = shape;
                            activeEdit = null;
                            activeShape = shape;
                            isResizing = true;
                            resizeHandlePosition = position;
                            dragStart = e.GetPosition(canvas);
                            startX = shape.X;
                            startY = shape.Y;
                            startWidth = shape.Width;
                            startHeight = shape.Height;
                            startCornerRadius = shape.CornerRadius;
                            startRotationDegrees = shape.RotationDegrees;
                            e.Pointer.Capture(canvas);
                            e.Handled = true;
                        };
                        Canvas.SetLeft(handle, point.X - 4.5);
                        Canvas.SetTop(handle, point.Y - 4.5);
                        return handle;
                    }

                    Border LineRotateHandle()
                    {
                        var center = new Point((startPoint.X + endPoint.X) / 2, (startPoint.Y + endPoint.Y) / 2);
                        var delta = endPoint - startPoint;
                        var length = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
                        var unitX = length > 0.01 ? -delta.Y / length : 0;
                        var unitY = length > 0.01 ? delta.X / length : -1;
                        var point = new Point(center.X + unitX * 28, center.Y + unitY * 28);
                        var handle = new Border
                        {
                            Width = 24,
                            Height = 24,
                            CornerRadius = new CornerRadius(12),
                            Background = Brushes.White,
                            BorderBrush = new SolidColorBrush(Color.Parse("#707070")),
                            BorderThickness = new Thickness(1),
                            Cursor = new Cursor(StandardCursorType.Hand),
                            IsVisible = selectedShape == shape,
                            Child = new TextBlock
                            {
                                Text = "↻",
                                FontSize = 16,
                                FontWeight = FontWeight.Bold,
                                Foreground = new SolidColorBrush(Color.Parse("#707070")),
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                            }
                        };
                        handle.PointerPressed += (_, e) =>
                        {
                            PushUndo();
                            selectedEdit = null;
                            selectedShape = shape;
                            activeEdit = null;
                            activeShape = shape;
                            isResizing = true;
                            resizeHandlePosition = "Rotate";
                            dragStart = e.GetPosition(canvas);
                            startX = shape.X;
                            startY = shape.Y;
                            startWidth = shape.Width;
                            startHeight = shape.Height;
                            startCornerRadius = shape.CornerRadius;
                            startRotationDegrees = shape.RotationDegrees;
                            e.Pointer.Capture(canvas);
                            e.Handled = true;
                        };
                        Canvas.SetLeft(handle, point.X - 12);
                        Canvas.SetTop(handle, point.Y - 12);
                        return handle;
                    }

                    var lineDeleteButton = new Button
                    {
                        Content = "削除",
                        FontSize = 12,
                        Padding = new Thickness(8, 3),
                        MinHeight = 24,
                        Background = actionBackgroundBrush,
                        Foreground = actionForegroundBrush,
                        BorderBrush = actionBorderBrush,
                        IsVisible = selectedShape == shape
                    };
                    lineDeleteButton.Click += (_, e) =>
                    {
                        DeleteSelectedShape(shape);
                        e.Handled = true;
                    };
                    Canvas.SetLeft(lineDeleteButton, Math.Max(0, Math.Min(startPoint.X, endPoint.X)));
                    Canvas.SetTop(lineDeleteButton, Math.Max(0, Math.Min(startPoint.Y, endPoint.Y) - 30));

                    void StartLineMove(PointerPressedEventArgs e)
                    {
                        PushUndo();
                        selectedEdit = null;
                        inlineEditingEdit = null;
                        selectedShape = shape;
                        LoadSelectedShape(shape);
                        activeEdit = null;
                        activeShape = shape;
                        isResizing = false;
                        dragStart = e.GetPosition(canvas);
                        startX = shape.X;
                        startY = shape.Y;
                        startWidth = shape.Width;
                        startHeight = shape.Height;
                        e.Pointer.Capture(canvas);
                        e.Handled = true;
                        RefreshOverlays();
                    }

                    hitLine.PointerPressed += (_, e) => StartLineMove(e);
                    line.PointerPressed += (_, e) => StartLineMove(e);
                    lineCanvas.Children.Add(hitLine);
                    lineCanvas.Children.Add(line);
                    lineCanvas.Children.Add(LineHandle("LineStart", startPoint));
                    lineCanvas.Children.Add(LineHandle("LineEnd", endPoint));
                    lineCanvas.Children.Add(LineRotateHandle());
                    lineCanvas.Children.Add(lineDeleteButton);
                    canvas.Children.Add(lineCanvas);
                    continue;
                }

                Control shapeControl = shape.ShapeType switch
                {
                    "Ellipse" => new Avalonia.Controls.Shapes.Ellipse
                    {
                        Fill = fill,
                        Stroke = stroke,
                        StrokeThickness = strokeThickness
                    },
                    "Line" => new Avalonia.Controls.Shapes.Line
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(Math.Max(1, width), Math.Max(1, height)),
                        Stroke = stroke,
                        StrokeThickness = Math.Max(1, strokeThickness)
                    },
                    "HorizontalLine" => new Avalonia.Controls.Shapes.Line
                    {
                        StartPoint = new Point(0, Math.Max(1, height) / 2),
                        EndPoint = new Point(Math.Max(1, width), Math.Max(1, height) / 2),
                        Stroke = stroke,
                        StrokeThickness = Math.Max(1, strokeThickness)
                    },
                    "RoundedRectangle" => new Avalonia.Controls.Shapes.Rectangle
                    {
                        Fill = fill,
                        Stroke = stroke,
                        StrokeThickness = strokeThickness,
                        RadiusX = Math.Min(
                            Math.Max(0, shape.CornerRadius / pageItem.PageWidthPoints * canvasWidth),
                            Math.Max(0, width / 2)),
                        RadiusY = Math.Min(
                            Math.Max(0, shape.CornerRadius / pageItem.PageHeightPoints * canvasHeight),
                            Math.Max(0, height / 2))
                    },
                    _ => new Avalonia.Controls.Shapes.Rectangle
                    {
                        Fill = fill,
                        Stroke = stroke,
                        StrokeThickness = strokeThickness
                    }
                };

                Border ResizeShapeHandle(string position, HorizontalAlignment horizontal, VerticalAlignment vertical)
                {
                    var handle = new Border
                    {
                        Width = 8,
                        Height = 8,
                        Background = Brushes.White,
                        BorderBrush = new SolidColorBrush(Color.Parse("#181818")),
                        BorderThickness = new Thickness(1),
                        HorizontalAlignment = horizontal,
                        VerticalAlignment = vertical,
                        Margin = new Thickness(-4),
                        Cursor = new Cursor(StandardCursorType.BottomRightCorner),
                        IsVisible = selectedShape == shape
                    };

                    handle.PointerPressed += (_, e) =>
                    {
                        PushUndo();
                        selectedEdit = null;
                        selectedShape = shape;
                        activeEdit = null;
                        activeShape = shape;
                        isResizing = true;
                        resizeHandlePosition = position;
                        dragStart = e.GetPosition(canvas);
                        startX = shape.X;
                        startY = shape.Y;
                        startWidth = shape.Width;
                        startHeight = shape.Height;
                        startCornerRadius = shape.CornerRadius;
                        startRotationDegrees = shape.RotationDegrees;
                        e.Pointer.Capture(canvas);
                        e.Handled = true;
                    };

                    return handle;
                }

                Border ShapeRotateHandle()
                {
                    var handle = new Border
                    {
                        Width = 24,
                        Height = 24,
                        CornerRadius = new CornerRadius(12),
                        Background = Brushes.White,
                        BorderBrush = new SolidColorBrush(Color.Parse("#707070")),
                        BorderThickness = new Thickness(1),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, -40, 0, 0),
                        Cursor = new Cursor(StandardCursorType.Hand),
                        IsVisible = selectedShape == shape,
                        Child = new TextBlock
                        {
                            Text = "↻",
                            FontSize = 16,
                            FontWeight = FontWeight.Bold,
                            Foreground = new SolidColorBrush(Color.Parse("#707070")),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    };

                    handle.PointerPressed += (_, e) =>
                    {
                        PushUndo();
                        selectedEdit = null;
                        selectedShape = shape;
                        activeEdit = null;
                        activeShape = shape;
                        isResizing = true;
                        resizeHandlePosition = "Rotate";
                        dragStart = e.GetPosition(canvas);
                        startX = shape.X;
                        startY = shape.Y;
                        startWidth = shape.Width;
                        startHeight = shape.Height;
                        startCornerRadius = shape.CornerRadius;
                        startRotationDegrees = shape.RotationDegrees;
                        e.Pointer.Capture(canvas);
                        e.Handled = true;
                    };

                    return handle;
                }

                Control RoundedCornerHandle()
                {
                    var handleLeft = Math.Clamp(
                        shape.CornerRadius / pageItem.PageWidthPoints * canvasWidth,
                        0,
                        Math.Max(0, width / 2));
                    var handle = new Border
                    {
                        Width = 14,
                        Height = 14,
                        CornerRadius = new CornerRadius(7),
                        Background = new SolidColorBrush(Color.Parse("#C8C8C8")),
                        BorderBrush = new SolidColorBrush(Color.Parse("#707070")),
                        BorderThickness = new Thickness(1),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(handleLeft - 7, -7, 0, 0),
                        Cursor = new Cursor(StandardCursorType.SizeWestEast)
                    };

                    handle.PointerPressed += (_, e) =>
                    {
                        PushUndo();
                        selectedEdit = null;
                        selectedShape = shape;
                        activeEdit = null;
                        activeShape = shape;
                        isResizing = true;
                        isAdjustingCornerRadius = true;
                        resizeHandlePosition = "CornerRadius";
                        dragStart = e.GetPosition(canvas);
                        startX = shape.X;
                        startY = shape.Y;
                        startWidth = shape.Width;
                        startHeight = shape.Height;
                        startCornerRadius = shape.CornerRadius;
                        startRotationDegrees = shape.RotationDegrees;
                        e.Pointer.Capture(canvas);
                        e.Handled = true;
                    };

                    var radiusLabel = new Border
                    {
                        Padding = new Thickness(8, 4),
                        CornerRadius = new CornerRadius(5),
                        Background = new SolidColorBrush(Color.Parse("#292929")),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(Math.Max(0, handleLeft + 10), -34, 0, 0),
                        IsVisible = isAdjustingCornerRadius && activeShape == shape,
                        Child = new TextBlock
                        {
                            Text = $"Radius: {shape.CornerRadius:0.#} pt",
                            FontSize = 12,
                            Foreground = Brushes.White
                        }
                    };

                    return new Grid
                    {
                        IsVisible = selectedShape == shape && shape.ShapeType == "RoundedRectangle",
                        Children =
                        {
                            radiusLabel,
                            handle
                        }
                    };
                }

                var shapeDeleteButton = new Button
                {
                    Content = "削除",
                    FontSize = 12,
                    Padding = new Thickness(8, 3),
                    MinHeight = 24,
                    Background = actionBackgroundBrush,
                    Foreground = actionForegroundBrush,
                    BorderBrush = actionBorderBrush,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    IsVisible = selectedShape == shape
                };
                shapeDeleteButton.Click += (_, e) =>
                {
                    DeleteSelectedShape(shape);
                    e.Handled = true;
                };

                var shapeBox = new Border
                {
                    Width = Math.Max(16, width),
                    Height = Math.Max(16, height),
                    Background = Brushes.Transparent,
                    BorderBrush = selectedShape == shape
                        ? new SolidColorBrush(Color.Parse("#202020"))
                        : Brushes.Transparent,
                    BorderThickness = selectedShape == shape ? new Thickness(2) : new Thickness(0),
                    Cursor = new Cursor(StandardCursorType.SizeAll),
                    Child = new Grid
                    {
                        Children =
                        {
                            shapeControl,
                            ResizeShapeHandle("TopLeft", HorizontalAlignment.Left, VerticalAlignment.Top),
                            ResizeShapeHandle("Top", HorizontalAlignment.Center, VerticalAlignment.Top),
                            ResizeShapeHandle("TopRight", HorizontalAlignment.Right, VerticalAlignment.Top),
                            ResizeShapeHandle("Left", HorizontalAlignment.Left, VerticalAlignment.Center),
                            ResizeShapeHandle("Right", HorizontalAlignment.Right, VerticalAlignment.Center),
                            ResizeShapeHandle("BottomLeft", HorizontalAlignment.Left, VerticalAlignment.Bottom),
                            ResizeShapeHandle("Bottom", HorizontalAlignment.Center, VerticalAlignment.Bottom),
                            ResizeShapeHandle("BottomRight", HorizontalAlignment.Right, VerticalAlignment.Bottom),
                            ShapeRotateHandle(),
                            RoundedCornerHandle(),
                            shapeDeleteButton
                        }
                    }
                };
                shapeBox.RenderTransformOrigin = RelativePoint.Center;
                shapeBox.RenderTransform = new RotateTransform(shape.RotationDegrees);
                shapeBox.PointerPressed += (_, e) =>
                {
                    PushUndo();
                    selectedEdit = null;
                    inlineEditingEdit = null;
                    selectedShape = shape;
                    LoadSelectedShape(shape);
                    activeEdit = null;
                    activeShape = shape;
                    isResizing = false;
                    dragStart = e.GetPosition(canvas);
                    startX = shape.X;
                    startY = shape.Y;
                    startWidth = shape.Width;
                    startHeight = shape.Height;
                    startCornerRadius = shape.CornerRadius;
                    startRotationDegrees = shape.RotationDegrees;
                    e.Pointer.Capture(canvas);
                    e.Handled = true;
                    RefreshOverlays();
                };
                Canvas.SetLeft(shapeBox, left);
                Canvas.SetTop(shapeBox, top);
                canvas.Children.Add(shapeBox);
            }

            foreach (var edit in pageEdits)
            {
                var left = edit.X / pageItem.PageWidthPoints * canvasWidth;
                var top = edit.Y / pageItem.PageHeightPoints * canvasHeight;
                var width = edit.Width / pageItem.PageWidthPoints * canvasWidth;
                var height = edit.Height / pageItem.PageHeightPoints * canvasHeight;
                IBrush previewTextBrush = IsNoColor(edit.TextColorHex)
                    ? Brushes.Transparent
                    : TryParseColor(edit.TextColorHex, out var previewTextColor)
                        ? new SolidColorBrush(previewTextColor)
                        : Brushes.Transparent;
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
                    var inlineTextBox = new TextBox
                    {
                        Text = edit.Text,
                        FontSize = Math.Max(8, edit.FontSize * canvasWidth / pageItem.PageWidthPoints),
                        FontFamily = PreviewFontFamily(edit.FontFamily),
                        FontWeight = edit.IsBold ? FontWeight.Bold : FontWeight.Normal,
                        Foreground = previewTextBrush,
                        TextAlignment = previewTextAlignment,
                        TextWrapping = TextWrapping.Wrap,
                        AcceptsReturn = false,
                        Background = Brushes.Transparent,
                        BorderBrush = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(3, 1, 12, 8),
                        MinHeight = 0
                    };
                    inlineTextBox.Classes.Add("pdf-inline-editor");
                    inlineTextBox.AddHandler(
                        PointerPressedEvent,
                        (_, e) =>
                        {
                            e.Handled = true;
                        },
                        RoutingStrategies.Tunnel);
                    inlineTextBox.TextChanged += (_, _) =>
                    {
                        edit.Text = inlineTextBox.Text ?? string.Empty;
                        if (selectedEdit == edit)
                        {
                            isLoadingSelection = true;
                            try
                            {
                                textBox.Text = edit.Text;
                            }
                            finally
                            {
                                isLoadingSelection = false;
                            }
                        }
                    };
                    inlineTextBox.KeyDown += (_, e) =>
                    {
                        if (HandleTextStyleShortcut(e))
                        {
                            return;
                        }

                        if (e.Key is Key.Enter or Key.Escape)
                        {
                            inlineEditingEdit = null;
                            e.Handled = true;
                            RefreshOverlays();
                        }
                    };
                    Dispatcher.UIThread.Post(() =>
                    {
                        inlineTextBox.Focus();
                        inlineTextBox.CaretIndex = inlineTextBox.Text?.Length ?? 0;
                    });
                    textControl = inlineTextBox;
                }
                else
                {
                    textControl = new TextBlock
                    {
                        Text = edit.Text,
                        FontSize = Math.Max(8, edit.FontSize * canvasWidth / pageItem.PageWidthPoints),
                        FontFamily = PreviewFontFamily(edit.FontFamily),
                        FontWeight = edit.IsBold ? FontWeight.Bold : FontWeight.Normal,
                        TextDecorations = edit.IsUnderline ? TextDecorations.Underline : null,
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
                        BorderBrush = new SolidColorBrush(Color.Parse("#181818")),
                        BorderThickness = new Thickness(1),
                        HorizontalAlignment = horizontal,
                        VerticalAlignment = vertical,
                        Margin = new Thickness(-4),
                        Cursor = new Cursor(StandardCursorType.BottomRightCorner),
                        IsVisible = selectedEdit == edit
                    };

                    handle.PointerPressed += (_, e) =>
                    {
                        PushUndo();
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
                    Background = actionBackgroundBrush,
                    Foreground = actionForegroundBrush,
                    BorderBrush = actionBorderBrush,
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
                    Background = edit.AddWhiteBox && !IsNoColor(edit.BackgroundColorHex) && TryParseColor(edit.BackgroundColorHex, out var bgColor)
                        ? new SolidColorBrush(bgColor)
                        : Brushes.Transparent,
                    BorderBrush = selectedEdit == edit
                        ? new SolidColorBrush(Color.Parse("#202020"))
                        : new SolidColorBrush(Color.Parse("#707070")),
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
                    selectedShape = null;
                    LoadSelectedEdit(edit);
                    canvas.Focus();
                    if (e.ClickCount >= 2)
                    {
                        PushUndo();
                        inlineEditingEdit = edit;
                        activeEdit = null;
                        canvas.Focus();
                        e.Handled = true;
                        RefreshOverlays();
                        return;
                    }

                    activeEdit = edit;
                    PushUndo();
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
            selectedShape = null;
            inlineEditingEdit = null;
            RefreshOverlays();
        };
        canvas.PointerMoved += (_, e) =>
        {
            if (activeEdit == null && activeShape == null)
            {
                return;
            }

            var position = e.GetPosition(canvas);
            var deltaX = (position.X - dragStart.X) / canvasWidth * pageItem.PageWidthPoints;
            var deltaY = (position.Y - dragStart.Y) / canvasHeight * pageItem.PageHeightPoints;

            if (isResizing)
            {
                if (activeShape != null && resizeHandlePosition == "Rotate")
                {
                    var pointerPdfX = position.X / canvasWidth * pageItem.PageWidthPoints;
                    var pointerPdfY = position.Y / canvasHeight * pageItem.PageHeightPoints;
                    var centerX = startX + startWidth / 2;
                    var centerY = startY + startHeight / 2;
                    var degrees = RotationFromPointer(centerX, centerY, pointerPdfX, pointerPdfY);
                    if (IsLineShape(activeShape))
                    {
                        var length = Math.Sqrt(startWidth * startWidth + startHeight * startHeight);
                        var radians = (degrees - 90) * Math.PI / 180;
                        var width = Math.Cos(radians) * length;
                        var height = Math.Sin(radians) * length;
                        activeShape.X = Math.Round(centerX - width / 2, 1);
                        activeShape.Y = Math.Round(centerY - height / 2, 1);
                        activeShape.Width = Math.Round(width, 1);
                        activeShape.Height = Math.Round(height, 1);
                        activeShape.RotationDegrees = 0;
                    }
                    else
                    {
                        activeShape.RotationDegrees = Math.Round(degrees, 1);
                    }

                    RefreshOverlays();
                    return;
                }

                if (activeShape != null && resizeHandlePosition == "CornerRadius")
                {
                    var radius = startCornerRadius + deltaX;
                    activeShape.CornerRadius = Math.Round(Math.Clamp(radius, 0, Math.Min(activeShape.Width, activeShape.Height) / 2), 1);
                    RefreshOverlays();
                    return;
                }

                if (activeShape != null && IsLineShape(activeShape))
                {
                    var pointerPdfX = Math.Clamp(position.X / canvasWidth * pageItem.PageWidthPoints, 0, pageItem.PageWidthPoints);
                    var pointerPdfY = Math.Clamp(position.Y / canvasHeight * pageItem.PageHeightPoints, 0, pageItem.PageHeightPoints);
                    if (resizeHandlePosition == "LineStart")
                    {
                        var endX = startX + startWidth;
                        var endY = startY + startHeight;
                        var startPoint = e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                            ? SnapPointTo45Degrees(endX, endY, pointerPdfX, pointerPdfY)
                            : new Point(pointerPdfX, pointerPdfY);
                        activeShape.X = Math.Round(startPoint.X, 1);
                        activeShape.Y = Math.Round(startPoint.Y, 1);
                        activeShape.Width = Math.Round(endX - startPoint.X, 1);
                        activeShape.Height = Math.Round(endY - startPoint.Y, 1);
                    }
                    else if (resizeHandlePosition == "LineEnd")
                    {
                        var endPoint = e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                            ? SnapPointTo45Degrees(startX, startY, pointerPdfX, pointerPdfY)
                            : new Point(pointerPdfX, pointerPdfY);
                        activeShape.Width = Math.Round(endPoint.X - startX, 1);
                        activeShape.Height = Math.Round(endPoint.Y - startY, 1);
                    }

                    RefreshOverlays();
                    return;
                }

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

                if (activeShape != null && !IsLineShape(activeShape) && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    KeepShapeAspectRatio(ref newX, ref newY, ref newWidth, ref newHeight);
                }

                if (activeEdit != null)
                {
                    activeEdit.X = Math.Round(newX, 1);
                    activeEdit.Y = Math.Round(newY, 1);
                    activeEdit.Width = Math.Round(newWidth, 1);
                    activeEdit.Height = Math.Round(newHeight, 1);
                }
                else if (activeShape != null)
                {
                    activeShape.X = Math.Round(newX, 1);
                    activeShape.Y = Math.Round(newY, 1);
                    activeShape.Width = Math.Round(newWidth, 1);
                    activeShape.Height = Math.Round(newHeight, 1);
                    if (activeShape.ShapeType == "RoundedRectangle")
                    {
                        activeShape.CornerRadius = Math.Round(ClampCornerRadius(activeShape), 1);
                    }
                }
            }
            else
            {
                if (activeEdit != null)
                {
                    activeEdit.X = Math.Clamp(startX + deltaX, 0, Math.Max(0, pageItem.PageWidthPoints - activeEdit.Width));
                    activeEdit.Y = Math.Clamp(startY + deltaY, 0, Math.Max(0, pageItem.PageHeightPoints - activeEdit.Height));
                }
                else if (activeShape != null)
                {
                    if (IsLineShape(activeShape))
                    {
                        var minX = Math.Min(startX, startX + startWidth);
                        var maxX = Math.Max(startX, startX + startWidth);
                        var minY = Math.Min(startY, startY + startHeight);
                        var maxY = Math.Max(startY, startY + startHeight);
                        var clampedDeltaX = Math.Clamp(deltaX, -minX, pageItem.PageWidthPoints - maxX);
                        var clampedDeltaY = Math.Clamp(deltaY, -minY, pageItem.PageHeightPoints - maxY);
                        activeShape.X = Math.Round(startX + clampedDeltaX, 1);
                        activeShape.Y = Math.Round(startY + clampedDeltaY, 1);
                    }
                    else
                    {
                        activeShape.X = Math.Clamp(startX + deltaX, 0, Math.Max(0, pageItem.PageWidthPoints - activeShape.Width));
                        activeShape.Y = Math.Clamp(startY + deltaY, 0, Math.Max(0, pageItem.PageHeightPoints - activeShape.Height));
                    }
                }
            }

            if (selectedEdit == activeEdit && activeEdit != null)
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
            else if (selectedShape == activeShape && activeShape != null)
            {
                isLoadingSelection = true;
                try
                {
                    widthBox.Text = activeShape.Width.ToString("0.#");
                    heightBox.Text = activeShape.Height.ToString("0.#");
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
            activeShape = null;
            isResizing = false;
            isAdjustingCornerRadius = false;
            e.Pointer.Capture(null);
            RefreshOverlays();
        };
        canvas.KeyDown += (_, e) =>
        {
            if (HandleTextStyleShortcut(e))
            {
                return;
            }

            if (inlineEditingEdit != null)
            {
                return;
            }

            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.C)
            {
                if (selectedEdit != null)
                {
                    copiedDraft = CloneTextEdit(selectedEdit);
                    e.Handled = true;
                }
                else if (selectedShape != null)
                {
                    copiedDraft = CloneShapeEdit(selectedShape);
                    e.Handled = true;
                }
            }
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.V)
            {
                switch (copiedDraft)
                {
                    case PdfTextEditDraft copiedText:
                        PushUndo();
                        var pastedText = CloneTextEdit(copiedText, 12);
                        pageEdits.Add(pastedText);
                        selectedEdit = pastedText;
                        selectedShape = null;
                        LoadSelectedEdit(pastedText);
                        RefreshOverlays();
                        e.Handled = true;
                        break;
                    case PdfShapeEditDraft copiedShape:
                        PushUndo();
                        var pastedShape = CloneShapeEdit(copiedShape, 12);
                        pageShapes.Add(pastedShape);
                        selectedEdit = null;
                        selectedShape = pastedShape;
                        LoadSelectedShape(pastedShape);
                        RefreshOverlays();
                        e.Handled = true;
                        break;
                }
            }
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Z)
            {
                RestoreUndo();
                e.Handled = true;
            }
        };
        canvas.PointerWheelChanged += (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                return;
            }

            ChangeZoom(e.Delta.Y > 0 ? 0.1 : -0.1);
            e.Handled = true;
        };
        Gestures.AddPointerTouchPadGestureMagnifyHandler(canvas, (_, e) =>
        {
            var delta = Math.Abs(e.Delta.Y) >= Math.Abs(e.Delta.X) ? e.Delta.Y : e.Delta.X;
            if (Math.Abs(delta) > 0.001)
            {
                ChangeZoom(delta > 0 ? 0.08 : -0.08);
            }

            e.Handled = true;
        });
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
            CornerRadius = new CornerRadius(999),
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

        var shapeButtons = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = 8,
            Children =
            {
                ShapeButton("四角", "Rectangle"),
                ShapeButton("角丸", "RoundedRectangle"),
                ShapeButton("丸", "Ellipse"),
                ShapeButton("線", "Line")
            }
        };
        Grid.SetColumn(shapeButtons.Children[1], 1);
        Grid.SetColumn(shapeButtons.Children[2], 2);
        Grid.SetColumn(shapeButtons.Children[3], 3);

        var shapeFillRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 8,
            Children =
            {
                shapeFillSwatch,
                shapeFillColorBox,
                chooseShapeFillColorButton
            }
        };
        Grid.SetColumn(shapeFillColorBox, 1);
        Grid.SetColumn(chooseShapeFillColorButton, 2);

        var shapeStrokeRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 8,
            Children =
            {
                shapeStrokeSwatch,
                shapeStrokeColorBox,
                chooseShapeStrokeColorButton
            }
        };
        Grid.SetColumn(shapeStrokeColorBox, 1);
        Grid.SetColumn(chooseShapeStrokeColorButton, 2);

        var shapeStrokeThicknessStepper = MacStepper(shapeStrokeThicknessBox, 1, " pt");
        var fontStyleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                boldToggle,
                underlineToggle
            }
        };

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
                fontStyleRow,
                new Border
                {
                    Height = 1,
                    Background = dialogFieldBorderBrush,
                    Margin = new Thickness(0, 4)
                },
                Label("テキスト色", 14, FontWeight.Bold),
                textColorRow,
                Label("背景色"),
                backgroundRow,
                new Border
                {
                    Height = 1,
                    Background = dialogFieldBorderBrush,
                    Margin = new Thickness(0, 4)
                },
                Label("図形", 15, FontWeight.Bold),
                shapeButtons,
                Label("塗り潰し色", 14, FontWeight.Bold),
                shapeFillRow,
                Label("境界線の色", 14, FontWeight.Bold),
                shapeStrokeRow,
                Label("境界線の太さ", 14, FontWeight.Bold),
                shapeStrokeThicknessStepper
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
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
                    Content = settingsPanel
                },
                doneButton
            }
        };
        Grid.SetRow(doneButton, 1);

        var zoomOutButton = new Button
        {
            Content = "−",
            Classes = { "small-action" },
            MinWidth = 42,
            MinHeight = 38,
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var zoomInButton = new Button
        {
            Content = "+",
            Classes = { "small-action" },
            MinWidth = 42,
            MinHeight = 38,
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        zoomOutButton.Click += (_, _) => ChangeZoom(-0.1);
        zoomInButton.Click += (_, _) => ChangeZoom(0.1);
        var zoomPanel = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 14, 14),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(14),
            Background = _isDarkTheme
                ? new SolidColorBrush(Color.FromArgb(190, 29, 32, 38))
                : new SolidColorBrush(Color.FromArgb(190, 248, 251, 255)),
            BorderBrush = _isDarkTheme
                ? new SolidColorBrush(Color.FromArgb(140, 120, 130, 145))
                : new SolidColorBrush(Color.FromArgb(150, 150, 170, 190)),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Children =
                        {
                            zoomOutButton,
                            zoomInButton
                        }
                    },
                    new TextBlock
                    {
                        Text = "ピンチ操作、またはCtrl + マウスホイールでも拡大縮小できます",
                        FontSize = 10,
                        Foreground = dialogMutedBrush,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 150
                    }
                }
            }
        };

        var previewPanel = new Grid
        {
            Children =
            {
                new ScrollViewer
                {
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
                    Content = canvas
                },
                zoomPanel
            }
        };

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
                    previewPanel,
                    rightPanel
                }
            }
        };
        dialog.KeyDown += (_, e) =>
        {
            if (HandleTextStyleShortcut(e))
            {
                return;
            }

            if (inlineEditingEdit == null && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Z)
            {
                RestoreUndo();
                e.Handled = true;
            }
        };

        async Task OpenColorChooserAsync(string title, TextBox targetBox, Button sourceButton, bool allowNoColor = false)
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
                    CornerRadius = new CornerRadius(999),
                    Background = new SolidColorBrush(Color.Parse(hex)),
                    BorderBrush = new SolidColorBrush(Color.Parse("#999999")),
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

            static Grid CreateNoColorIcon(IBrush borderBrush)
            {
                return new Grid
                {
                    Width = 24,
                    Height = 24,
                    Children =
                    {
                        new Border
                        {
                            Width = 22,
                            Height = 22,
                            CornerRadius = new CornerRadius(6),
                            Background = Brushes.White,
                            BorderBrush = borderBrush,
                            BorderThickness = new Thickness(1.5),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        new Avalonia.Controls.Shapes.Line
                        {
                            StartPoint = new Point(4, 20),
                            EndPoint = new Point(20, 4),
                            Stroke = new SolidColorBrush(Color.Parse("#555555")),
                            StrokeThickness = 3.5,
                            StrokeLineCap = PenLineCap.Round
                        }
                    }
                };
            }

            if (allowNoColor)
            {
                var noneButton = new Button
                {
                    Width = 30,
                    Height = 30,
                    MinWidth = 30,
                    MinHeight = 30,
                    Padding = new Thickness(0),
                    Margin = new Thickness(3),
                    CornerRadius = new CornerRadius(999),
                    Background = Brushes.Transparent,
                    BorderBrush = new SolidColorBrush(Color.Parse("#999999")),
                    BorderThickness = new Thickness(1),
                    Content = CreateNoColorIcon(new SolidColorBrush(Color.Parse("#181818")))
                };
                ToolTip.SetTip(noneButton, "色なし");
                noneButton.Click += (_, _) =>
                {
                    targetBox.Text = "None";
                    UpdateSwatch();
                    ApplyFieldsToSelected();
                    chooser.Close();
                };
                palette.Children.Add(noneButton);
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
            await OpenColorChooserAsync("テキスト色", textColorBox, chooseTextColorButton, allowNoColor: true);
        chooseBackgroundColorButton.Click += async (_, _) =>
            await OpenColorChooserAsync("背景色", backgroundColorBox, chooseBackgroundColorButton, allowNoColor: true);
        chooseShapeFillColorButton.Click += async (_, _) =>
            await OpenColorChooserAsync("塗り潰し色", shapeFillColorBox, chooseShapeFillColorButton, allowNoColor: true);
        chooseShapeStrokeColorButton.Click += async (_, _) =>
            await OpenColorChooserAsync("境界線の色", shapeStrokeColorBox, chooseShapeStrokeColorButton, allowNoColor: true);

        Grid.SetColumn(backgroundColorBox, 1);
        doneButton.Click += (_, _) => dialog.Close();
        RefreshOverlays();
        await dialog.ShowDialog(this);

        _pdfTextEdits.RemoveAll(edit => edit.PageNumber == pageItem.PageNumber);
        _pdfTextEdits.AddRange(pageEdits);
        _pdfShapeEdits.RemoveAll(shape => shape.PageNumber == pageItem.PageNumber);
        _pdfShapeEdits.AddRange(pageShapes);
        SetStatus($"{pageItem.PageLabel}の編集を一時保存しました。全ページの編集後、開始で書き出します。");
    }

}
