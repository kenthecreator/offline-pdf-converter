using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OfflinePDFConverter.Models;
using OfflinePDFConverter.Services;

namespace OfflinePDFConverter.Views;
public partial class MainWindow
{
    private async Task RunBatchAsync(IReadOnlyList<string> files,
        Func<string, IProgress<ConversionProgress>, CancellationToken, Task<ConversionResult>> convert)
    {
        if (_conversionCts != null) return;
        var pending = files;
        while (pending.Count > 0)
        {
            ConversionResult? result = null;
            _conversionCts = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                result = await BatchRunner.RunAsync(pending, convert,
                    new Progress<ConversionProgress>(UpdateProgress), _conversionCts.Token);
                _mainProgressBar.Value = result.Items.Any(x => x.Status is FileConversionStatus.Cancelled or FileConversionStatus.NotStarted) ? _mainProgressBar.Value : 100;
                SetStatus("一括処理の結果を確認してください。");
            }
            catch (Exception ex) { await ShowMessageAsync("処理できませんでした", FriendlyErrorFormatter.ToUserMessage(ex)); }
            finally { _conversionCts.Dispose(); _conversionCts = null; SetBusy(false); }
            if (result == null) return;
            pending = await ShowBatchResultAsync(result);
        }
    }

    private async Task<IReadOnlyList<string>> ShowBatchResultAsync(ConversionResult result)
    {
        var retryFiles = result.Items.Where(x => x.Status == FileConversionStatus.Failed).Select(x => x.SourcePath).ToArray();
        var remaining = result.Items.Where(x => x.Status is FileConversionStatus.Cancelled or FileConversionStatus.NotStarted).Select(x => x.SourcePath).ToArray();
        var dialog = new Window { Title = "一括処理の結果", Width = 720, Height = 460, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock { Text = $"成功 {result.Items.Count(x => x.Status == FileConversionStatus.Succeeded)}件 / 失敗 {retryFiles.Length}件 / 中止・未処理 {remaining.Length}件", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "入力ファイルごとの結果です。再実行時、保存済みの出力は残して別名で保存します。", TextWrapping = TextWrapping.Wrap });
        foreach (var item in result.Items)
        {
            var status = item.Status switch { FileConversionStatus.Succeeded => "成功", FileConversionStatus.Failed => "失敗", FileConversionStatus.Cancelled => "中止", _ => "未処理" };
            body.Children.Add(new TextBlock { Text = $"{status}  {item.SourcePath}\n{item.Message}", TextWrapping = TextWrapping.Wrap });
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var retry = new Button { Content = "失敗だけ再実行", IsEnabled = retryFiles.Length > 0 };
        var resume = new Button { Content = "中止・未処理を再実行", IsEnabled = remaining.Length > 0 };
        var close = new Button { Content = "閉じる" };
        retry.Click += (_, _) => dialog.Close(retryFiles);
        resume.Click += (_, _) => dialog.Close(remaining);
        close.Click += (_, _) => dialog.Close(Array.Empty<string>());
        buttons.Children.Add(retry); buttons.Children.Add(resume); buttons.Children.Add(close);
        var grid = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 16 };
        grid.Children.Add(new ScrollViewer { Content = body }); Grid.SetRow(buttons, 1); grid.Children.Add(buttons); dialog.Content = grid;
        return await dialog.ShowDialog<string[]?>(this) ?? Array.Empty<string>();
    }
    private void SetBusy(bool busy)
    {
        if (this.FindControl<Button>("CancelConversionButton") is { } cancel) cancel.IsVisible = busy;
        if (this.FindControl<Button>("OfflineOcrButton") is { } ocr) ocr.IsEnabled = !busy;
        if (this.FindControl<ComboBox>("OutputPresetCombo") is { } preset) preset.IsEnabled = !busy;
        if (this.FindControl<NumericUpDown>("JpegQualityInput") is { } quality) quality.IsEnabled = !busy;
        _startPdfButton.IsEnabled = !busy;
        _startImageButton.IsEnabled = !busy;
        _startPdfToolButton.IsEnabled = !busy;
        _pdfModeButton.IsEnabled = !busy;
        _pdfToolsModeButton.IsEnabled = !busy;
        _modeSelectorSwitch.IsEnabled = !busy;
        _pdfToImageDirectionButton.IsEnabled = !busy;
        _imageToPdfDirectionButton.IsEnabled = !busy;
        _directionSelectorSwitch.IsEnabled = !busy;
        _pdfPreviewIconButton.IsEnabled = !busy;
        _pdfPreviewListButton.IsEnabled = !busy;
        _pdfPreviewSelectorSwitch.IsEnabled = !busy;
        _pdfDpiSliderSwitch.IsEnabled = !busy;
        _pdfPageRangeTextBox.IsEnabled = !busy;
    }

}
