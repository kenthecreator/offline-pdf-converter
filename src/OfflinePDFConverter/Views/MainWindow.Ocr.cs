using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OfflinePDFConverter.Models;
using OfflinePDFConverter.Services;
namespace OfflinePDFConverter.Views;
public partial class MainWindow
{
    private async void OnOfflineOcrClick(object? sender, RoutedEventArgs e)
    {
        if (_conversionCts != null) return;
        var files = _pdfFiles.Select(x => x.Path).ToArray();
        if (files.Length == 0) { await ShowMessageAsync("文字認識", "PDFを追加してください。"); return; }
        var passwords = GetPdfPasswords();
        var folder = new TextBox { Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Watermark = "TXTの保存先フォルダ" };
        var pages = new TextBox { Watermark = "認識ページ：空欄ですべて／例 1,3,5-7" };
        var language = new ComboBox { ItemsSource = new[] { "日本語・英語（横書き）", "日本語・英語（縦書き）", "英語" }, SelectedIndex = 0 };
        var start = new Button { Content = "文字認識を開始" };
        var cancel = new Button { Content = "閉じる" };
        var licenses = new Button { Content = "使用ライセンス" };
        var dialog = new Window { Title = "オフライン文字認識（OCR）", Width = 660, Height = 440, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
        body.Children.Add(new TextBlock { Text = "スキャンPDFを文字認識して、PDFごとにTXTを保存します。文字や数字は原本と照合してください。ページ内の範囲選択は使いません。", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = OperatingSystem.IsMacOS()
            ? "Macではインストール済みのTesseractとアプリ内の日英認識データを使用します。認識はローカルで行われます。"
            : "文字認識機能はアプリに内蔵されています。追加インストール・ダウンロード・外部送信はありません。", TextWrapping = TextWrapping.Wrap });
        foreach (var entry in new (string Label, Control Input)[] { ("TXTの保存先", folder), ("言語", language), ("ページ指定", pages) })
        {
            body.Children.Add(new TextBlock { Text = entry.Label });
            if (entry.Input is TextBox box && box != pages)
            {
                var browse = new Button { Content = "選択" };
                browse.Click += async (_, _) =>
                {
                    var selected = await dialog.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = entry.Label, AllowMultiple = false });
                    if (selected.FirstOrDefault()?.TryGetLocalPath() is { } path) box.Text = path;
                };
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
                row.Children.Add(box); Grid.SetColumn(browse, 1); row.Children.Add(browse); body.Children.Add(row);
            }
            else body.Children.Add(entry.Input);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 }; buttons.Children.Add(start); buttons.Children.Add(cancel); buttons.Children.Add(licenses); body.Children.Add(buttons);
        dialog.Content = new ScrollViewer { Content = body };
        licenses.Click += async (_, _) =>
        {
            try
            {
                using var package = await Task.Run(() => BundledOcrRuntime.ExtractEmbedded());
                var licenseFolder = System.IO.Path.Combine(package.RootDirectory, "licenses");
                var text = string.Join("\n\n", System.IO.Directory.GetFiles(licenseFolder).OrderBy(x => x)
                    .Select(path => System.IO.Path.GetFileName(path) + "\n\n" + System.IO.File.ReadAllText(path)));
                var viewer = new Window { Title = "内蔵OCRの使用ライセンス", Width = 760, Height = 560,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new TextBox { Margin = new Thickness(16), Text = text, IsReadOnly = true,
                        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap } };
                await viewer.ShowDialog(dialog);
            }
            catch (Exception ex) { await ShowMessageAsync("ライセンスを表示できませんでした", FriendlyErrorFormatter.ToUserMessage(ex)); }
        };
        start.Click += (_, _) => dialog.Close(true); cancel.Click += (_, _) => dialog.Close(false);
        if (!await dialog.ShowDialog<bool>(this)) return;
        var selectedLanguage = language.SelectedIndex switch { 1 => "jpn_vert+eng", 2 => "eng", _ => "jpn+eng" };
        if (string.IsNullOrWhiteSpace(folder.Text)) { await ShowMessageAsync("保存先", "TXTの保存先フォルダを指定してください。"); return; }
        var service = new OfflineOcrService();
        await RunBatchAsync(files, (file, progress, token) => service.ExtractBundledAsync(file,
            System.IO.Path.Combine(folder.Text.Trim(), System.IO.Path.GetFileNameWithoutExtension(file) + "_OCR.txt"),
            selectedLanguage, pages.Text?.Trim() ?? "",
            passwords.TryGetValue(file, out var password) ? password : "", progress, token));
    }

    private void OnCancelConversionClick(object? sender, RoutedEventArgs e)
    {
        _conversionCts?.Cancel();
        SetStatus("中止しています。現在の処理が終了するまでお待ちください。");
    }
}
