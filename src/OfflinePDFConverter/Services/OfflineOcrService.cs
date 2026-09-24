using System.Diagnostics;
using System.Text;
using OfflinePDFConverter.Models;
using PDFtoImage;
using SkiaSharp;
namespace OfflinePDFConverter.Services;

#pragma warning disable CA1416
public sealed class OfflineOcrService
{
    public Task<ConversionResult> ExtractBundledAsync(string pdfPath, string outputPath, string language,
        string pages, string password, IProgress<ConversionProgress> progress, CancellationToken token)
        => Task.Run(async () =>
        {
            progress.Report(new(0, 1, "内蔵の文字認識機能を準備しています..."));
            if (OperatingSystem.IsWindows())
            {
                var runtime = BundledOcrRuntime.Get(token);
                return await ExtractAsync(new(pdfPath, outputPath, runtime.EnginePath, runtime.DataDirectory,
                    language, pages, password), progress, token);
            }

            if (OperatingSystem.IsMacOS())
            {
                var engine = FindMacTesseract()
                    ?? throw new FileNotFoundException("MacでOCRを使うにはTesseractをインストールしてください。日本語・英語の認識データはアプリ内のものを使用します。");
                using var runtime = BundledOcrRuntime.ExtractEmbedded(token);
                return await ExtractAsync(new(pdfPath, outputPath, engine, runtime.DataDirectory,
                    language, pages, password), progress, token);
            }

            throw new PlatformNotSupportedException("このOCR機能はWindowsとMacに対応しています。");
        }, token);

    private static string? FindMacTesseract()
    {
        var candidates = new[] { "/opt/homebrew/bin/tesseract", "/usr/local/bin/tesseract" }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(directory => Path.Combine(directory, "tesseract")));
        return candidates.FirstOrDefault(File.Exists);
    }

    public Task<ConversionResult> ExtractAsync(OcrRequest request, IProgress<ConversionProgress> progress, CancellationToken token)
        => Task.Run(async () =>
        {
            Validate(request);
            using var countInput = File.OpenRead(request.PdfPath);
            var password = string.IsNullOrEmpty(request.Password) ? null : request.Password;
            var count = Conversion.GetPageCount(countInput, password: password);
            var pages = string.IsNullOrWhiteSpace(request.Pages)
                ? Enumerable.Range(1, count).ToArray()
                : PageRangeParser.Parse(request.Pages, count, "認識する").ToArray();
            using var input = File.OpenRead(request.PdfPath);
            var directory = Path.Combine(Path.GetTempPath(), "OfflinePDFConverter-OCR-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var builder = new StringBuilder();
                var completed = 0;
                foreach (var bitmap in Conversion.ToImages(input, pages.Select(x => x - 1).ToArray(), password: password,
                    options: new RenderOptions(Dpi: 300, WithAnnotations: true, BackgroundColor: SKColors.White, UseTiling: true)))
                {
                    var imagePath = Path.Combine(directory, "page.png");
                    using (bitmap)
                    {
                        token.ThrowIfCancellationRequested();
                        using var stream = File.Create(imagePath);
                        bitmap.Encode(stream, SKEncodedImageFormat.Png, 100);
                    }
                    progress.Report(new(completed, pages.Length, $"{Path.GetFileName(request.PdfPath)}: {pages[completed]}ページ目を文字認識しています"));
                    var text = await RecognizeImageAsync(request.EnginePath, request.DataDirectory, request.Language, imagePath, token);
                    builder.AppendLine($"--- {pages[completed]}ページ目（OCR・要確認） ---");
                    builder.AppendLine(string.IsNullOrWhiteSpace(text) ? "[文字を認識できませんでした]" : text.Trim());
                    builder.AppendLine(); completed++;
                    progress.Report(new(completed, pages.Length, $"{completed}/{pages.Length}ページの文字認識が完了しました"));
                }
                AtomicFile.Write(request.OutputPath, path => File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false)), token);
                return new ConversionResult(1, Array.Empty<string>());
            }
            finally { Directory.Delete(directory, recursive: true); }
        }, token);

    public static void Validate(OcrRequest request)
    {
        if (!File.Exists(request.PdfPath)) throw new FileNotFoundException("PDFが見つかりません。", request.PdfPath);
        if (!Path.IsPathFullyQualified(request.EnginePath) || !File.Exists(request.EnginePath))
            throw new ArgumentException("Tesseractの実行ファイルをフルパスで指定してください。");
        if (request.Language is not ("jpn+eng" or "jpn_vert+eng" or "eng"))
            throw new ArgumentException("認識言語を選択してください。");
        foreach (var language in request.Language.Split('+'))
            if (!File.Exists(Path.Combine(request.DataDirectory, language + ".traineddata")))
                throw new ArgumentException($"認識データ {language}.traineddata が見つかりません。認識データのフォルダを確認してください。");
        if (string.IsNullOrWhiteSpace(request.OutputPath)) throw new ArgumentException("保存先を指定してください。");
        if (string.Equals(Path.GetFullPath(request.OutputPath), Path.GetFullPath(request.PdfPath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("元のPDFとは別の保存先を指定してください。");
    }

    public static async Task<string> RecognizeImageAsync(string enginePath, string dataDirectory, string language, string imagePath, CancellationToken token)
    {
        var info = new ProcessStartInfo(enginePath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(enginePath))!,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { imagePath, "stdout", "--tessdata-dir", dataDirectory, "-l", language, "--psm", language.StartsWith("jpn_vert") ? "5" : "3" })
            info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        token.ThrowIfCancellationRequested();
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, error);
            token.ThrowIfCancellationRequested();
            throw new TimeoutException("1ページの文字認識が3分を超えたため中止しました。");
        }
        var recognized = await output;
        var diagnostic = await error;
        if (process.ExitCode != 0) throw new IOException($"文字認識に失敗しました（終了コード {process.ExitCode}）。{diagnostic}");
        return recognized;
    }
}
