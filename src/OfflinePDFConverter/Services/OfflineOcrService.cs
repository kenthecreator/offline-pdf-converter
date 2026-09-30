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
#if PADDLE_OCR
        => ExtractUsingRecognizerAsync(pdfPath, outputPath, language, pages, password, progress, token,
            (image, cancellation) => PaddleOcrService.RecognizeImageAsync(image, language, cancellation));
#else
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
#endif

    public Task<ConversionResult> CreateSearchablePdfBundledAsync(string pdfPath, string outputPath, string language,
        string pages, string password, IProgress<ConversionProgress> progress, CancellationToken token)
        => Task.Run(async () =>
        {
#if PADDLE_OCR
            return await SearchablePdfService.CreateAsync(pdfPath, outputPath, language, pages, password, progress, token,
                (image, cancellation) => PaddleOcrService.RecognizeBlocksAsync(image, language, cancellation));
#else
            BundledOcrRuntime? extracted = null;
            try
            {
                var package = OperatingSystem.IsWindows() ? BundledOcrRuntime.Get(token)
                    : extracted = BundledOcrRuntime.ExtractEmbedded(token);
                var engine = OperatingSystem.IsWindows() ? package.EnginePath : FindMacTesseract()
                    ?? throw new FileNotFoundException("MacでOCRを使うにはTesseractをインストールしてください。");
                return await SearchablePdfService.CreateAsync(pdfPath, outputPath, language, pages, password, progress, token,
                    async (image, cancellation) => SearchablePdfService.ParseTsv(await RecognizeImageAsync(
                        engine, package.DataDirectory, language, image, cancellation, "tsv")));
            }
            finally { extracted?.Dispose(); }
#endif
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
    {
        Validate(request);
        return ExtractUsingRecognizerAsync(request.PdfPath, request.OutputPath, request.Language, request.Pages,
            request.Password, progress, token, (image, cancellation) => RecognizeImageAsync(request.EnginePath,
                request.DataDirectory, request.Language, image, cancellation));
    }

    private static Task<ConversionResult> ExtractUsingRecognizerAsync(string pdfPath, string outputPath, string language,
        string pageSelection, string pdfPassword, IProgress<ConversionProgress> progress, CancellationToken token,
        Func<string, CancellationToken, Task<string>> recognize)
        => Task.Run(async () =>
        {
            ValidateDocument(pdfPath, outputPath, language);
            token.ThrowIfCancellationRequested();
            using var countInput = File.OpenRead(pdfPath);
            var password = string.IsNullOrEmpty(pdfPassword) ? null : pdfPassword;
            var count = Conversion.GetPageCount(countInput, password: password);
            var pages = string.IsNullOrWhiteSpace(pageSelection)
                ? Enumerable.Range(1, count).ToArray()
                : PageRangeParser.Parse(pageSelection, count, "認識する").ToArray();
            using var input = File.OpenRead(pdfPath);
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
                    progress.Report(new(completed, pages.Length, $"{Path.GetFileName(pdfPath)}: {pages[completed]}ページ目を文字認識しています"));
                    var text = await recognize(imagePath, token);
                    builder.AppendLine($"--- {pages[completed]}ページ目（OCR・要確認） ---");
                    builder.AppendLine(string.IsNullOrWhiteSpace(text) ? "[文字を認識できませんでした]" : text.Trim());
                    builder.AppendLine(); completed++;
                    progress.Report(new(completed, pages.Length, $"{completed}/{pages.Length}ページの文字認識が完了しました"));
                }
                AtomicFile.Write(outputPath, path => File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false)), token);
                return new ConversionResult(1, Array.Empty<string>());
            }
            finally { Directory.Delete(directory, recursive: true); }
        }, token);

    public static void Validate(OcrRequest request)
    {
        ValidateDocument(request.PdfPath, request.OutputPath, request.Language);
        if (!Path.IsPathFullyQualified(request.EnginePath) || !File.Exists(request.EnginePath))
            throw new ArgumentException("Tesseractの実行ファイルをフルパスで指定してください。");
        foreach (var language in request.Language.Split('+'))
            if (!File.Exists(Path.Combine(request.DataDirectory, language + ".traineddata")))
                throw new ArgumentException($"認識データ {language}.traineddata が見つかりません。認識データのフォルダを確認してください。");
    }

    private static void ValidateDocument(string pdfPath, string outputPath, string language)
    {
        if (!File.Exists(pdfPath)) throw new FileNotFoundException("PDFが見つかりません。", pdfPath);
        if (language is not ("jpn" or "jpn_vert" or "jpn+eng" or "jpn_vert+eng" or "eng")) throw new ArgumentException("認識言語を選択してください。");
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("保存先を指定してください。");
        if (string.Equals(Path.GetFullPath(outputPath), Path.GetFullPath(pdfPath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("元のPDFとは別の保存先を指定してください。");
    }

    public static async Task<string> RecognizeImageAsync(string enginePath, string dataDirectory, string language, string imagePath, CancellationToken token, string? outputFormat = null)
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
        if (outputFormat == "tsv")
        {
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add("tessedit_create_tsv=1");
        }
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
