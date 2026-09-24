using System.Text.Json;
using OfflinePDFConverter.Models;

namespace OfflinePDFConverter.Services;

/// <summary>Runs from the shipped exe, so a clean Windows machine can test the actual package.</summary>
internal static class OfflineSelfTest
{
    public static async Task<int> RunAsync(string reportPath)
    {
        var root = Directory.CreateTempSubdirectory("OfflinePDFConverter-selftest-").FullName;
        var checks = new List<string>();
        Exception? failure = null;
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windowsで実行してください。");
            var runtime = BundledOcrRuntime.Get();
            checks.Add("embedded OCR payload verified and extracted");
            var imagePath = Path.Combine(root, "日本語テスト.png");
            using (var resource = typeof(OfflineSelfTest).Assembly.GetManifestResourceStream("OfflinePDFConverter.OcrSelfTest.png")
                   ?? throw new InvalidDataException("検証画像がありません。"))
            using (var output = File.Create(imagePath)) await resource.CopyToAsync(output);
            var progress = new SilentProgress();
            var result = await new ImageToPdfService().ConvertAsync(new(new[] { imagePath },
                Path.Combine(root, "検証.pdf"), ImagePageMode.A4Portrait, true), progress, default);
            if (result.HasErrors) throw new IOException(string.Join("\n", result.Errors));
            checks.Add("image to PDF");
            var pdf = Directory.GetFiles(root, "*.pdf").Single();
            var textPath = Path.Combine(root, "認識結果.txt");
            await new OfflineOcrService().ExtractBundledAsync(pdf, textPath, "jpn+eng", "1", "", progress, default);
            var text = await File.ReadAllTextAsync(textPath);
            if (!text.Contains("日本語") || !text.Contains("12345")) throw new InvalidDataException("日本語OCRの期待結果と一致しません: " + text);
            checks.Add("PDF rendering and bundled Japanese/English OCR");
            checks.Add("Unicode file paths");
            if (typeof(OfflineSelfTest).Assembly.GetName().Version?.ToString() != "3.2.0.0") throw new InvalidDataException("バージョンが一致しません。");
            checks.Add("version 3.2.0.0");
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            BundledOcrRuntime.Cleanup();
            Directory.Delete(root, recursive: true);
        }
        var report = new { version = "3.2.0", passed = failure == null, checks, error = failure?.ToString(),
            operatingSystem = Environment.OSVersion.ToString(), architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString() };
        var fullReportPath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullReportPath)!);
        await File.WriteAllTextAsync(fullReportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return failure == null ? 0 : 1;
    }
    private sealed class SilentProgress : IProgress<ConversionProgress> { public void Report(ConversionProgress value) { } }
}
