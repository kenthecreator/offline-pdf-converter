using System.Text.Json;
using System.Diagnostics;
using System.Security.Cryptography;
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
#if PADDLE_OCR
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("WindowsまたはMacで実行してください。");
            using var runtime = BundledPaddleOcrRuntime.ExtractEmbedded();
#else
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windowsで実行してください。");
            var runtime = BundledOcrRuntime.Get();
#endif
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
            var searchable = Path.Combine(root, "認識結果.pdf");
            await new OfflineOcrService().CreateSearchablePdfBundledAsync(pdf, searchable, "jpn+eng", "1", "", progress, default);
            using var recognized = UglyToad.PdfPig.PdfDocument.Open(searchable);
            var text = recognized.GetPage(1).Text;
            if (!text.Contains("日本語") || !text.Contains("12345")) throw new InvalidDataException("日本語OCRの期待結果と一致しません: " + text);
            checks.Add("PDF rendering and bundled Japanese/English searchable PDF OCR");
#if PADDLE_OCR
            if (OperatingSystem.IsWindows()) VerifyAppLocalCrt(checks);
#endif
            checks.Add("Unicode file paths");
            if (typeof(OfflineSelfTest).Assembly.GetName().Version?.ToString() != "4.0.0.0") throw new InvalidDataException("バージョンが一致しません。");
            checks.Add("version 4.0.0.0");
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
#if PADDLE_OCR
            PaddleOcrService.Cleanup();
#endif
            BundledOcrRuntime.Cleanup();
            Directory.Delete(root, recursive: true);
        }
        var report = new { version = "4.0.0", edition = AppIdentity.WindowTitle, passed = failure == null, checks, error = failure?.ToString(),
            operatingSystem = Environment.OSVersion.ToString(), architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString() };
        var fullReportPath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullReportPath)!);
        await File.WriteAllTextAsync(fullReportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return failure == null ? 0 : 1;
    }
    private sealed class SilentProgress : IProgress<ConversionProgress> { public void Report(ConversionProgress value) { } }

#if PADDLE_OCR
    private static void VerifyAppLocalCrt(List<string> checks)
    {
        using var manifest = typeof(OfflineSelfTest).Assembly.GetManifestResourceStream("OfflinePDFConverter.WindowsCrtManifest.json")
            ?? throw new InvalidDataException("The standalone Windows CRT manifest is missing.");
        using var document = JsonDocument.Parse(manifest);
        using var process = Process.GetCurrentProcess();
        var modules = process.Modules.Cast<ProcessModule>().ToArray();
        var onnx = modules.Single(m => m.ModuleName.Equals("onnxruntime.dll", StringComparison.OrdinalIgnoreCase));
        var directory = Path.GetDirectoryName(onnx.FileName)!;
        foreach (var entry in document.RootElement.GetProperty("files").EnumerateArray())
        {
            var name = entry.GetProperty("file").GetString()!;
            var path = Path.Combine(directory, name);
            using var file = File.OpenRead(path);
            if (!Convert.ToHexString(SHA256.HashData(file)).Equals(entry.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Extracted CRT hash mismatch: " + name);
            foreach (var module in modules.Where(m => m.ModuleName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                if (!Path.GetFullPath(module.FileName).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("OCR loaded a system CRT instead of the embedded copy: " + module.FileName);
        }
        foreach (var name in new[] { "msvcp140.dll", "vcruntime140.dll" })
            if (!modules.Any(m => m.ModuleName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Expected loaded CRT module missing: " + name);
        checks.Add("embedded x64 CRT hashes verified; loaded CRT modules are app-local, not system-installed");
    }
#endif
}
