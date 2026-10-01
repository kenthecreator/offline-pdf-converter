using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OfflinePDFConverter.Models;
using OfflinePDFConverter.Services;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

if (args.Length != 2) throw new ArgumentException("Pass the repository directory and evidence directory.");
var repository = Path.GetFullPath(args[0]);
var evidence = Path.GetFullPath(args[1]);
Directory.CreateDirectory(evidence);
var scratch = Directory.CreateTempSubdirectory("PaddleEdition-tests-").FullName;
var checks = new List<object>();
var rows = new List<object>();
var failed = 0;
var progress = new SilentProgress();
var service = new OfflineOcrService();
var fixtures = Path.Combine(repository, "artifacts/ocr-benchmark/fixtures");
using var fixtureManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, "manifest.json")));
var cases = fixtureManifest.RootElement.GetProperty("cases").EnumerateArray().ToArray();
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
async Task Test(string name, Func<Task> operation)
{
    var watch = Stopwatch.StartNew();
    try { await operation(); checks.Add(new { name, passed = true, seconds = watch.Elapsed.TotalSeconds }); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; checks.Add(new { name, passed = false, error = ex.ToString() }); Console.WriteLine("FAIL " + name + " " + ex); }
}
string Normalize(string text) => string.Concat(text.Normalize(NormalizationForm.FormKC).Where(c => !char.IsWhiteSpace(c)));
int Distance(string a, string b)
{
    var previous = Enumerable.Range(0, b.Length + 1).ToArray();
    for (var i = 0; i < a.Length; i++)
    {
        var next = new int[b.Length + 1]; next[0] = i + 1;
        for (var j = 0; j < b.Length; j++) next[j + 1] = Math.Min(Math.Min(next[j] + 1, previous[j + 1] + 1), previous[j] + (a[i] == b[j] ? 0 : 1));
        previous = next;
    }
    return previous[^1];
}
string Body(string text) => string.Join("\n", text.Split('\n').Where(line => !line.StartsWith("--- ") && line.Trim() != "[文字を認識できませんでした]"));
var suitePdf = "";
try
{
    await Test("edition identity, embedded hashes and offline licenses", () =>
    {
        Check(AppIdentity.WindowTitle == "Offline PDF Converter (v4.1)", "wrong edition");
        using var package = BundledPaddleOcrRuntime.ExtractEmbedded();
        Check(File.Exists(Path.Combine(package.RootDirectory, "rec.onnx")), "recognizer missing");
        Check(AppIdentity.ReadOcrLicenses().Contains("Apache License"), "license viewer missing");
        Check(!typeof(AppIdentity).Assembly.GetManifestResourceNames().Contains("OfflinePDFConverter.WindowsOcr.zip"), "Tesseract payload unexpectedly bundled");
        return Task.CompletedTask;
    });
    await Test("generate nine-page raster PDF using application image-to-PDF service", async () =>
    {
        Check(cases.Single(c => c.GetProperty("id").GetString() == "vertical").GetProperty("language").GetString() == "jpn_vert+eng", "vertical fixture configuration missing");
        Check(cases.Single(c => c.GetProperty("id").GetString() == "two_columns").GetProperty("layout").GetString() == "two_columns", "two-column fixture configuration missing");
        var images = cases.Select(c => Path.Combine(fixtures, c.GetProperty("image").GetString()!)).ToArray();
        foreach (var c in cases)
            Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(fixtures, c.GetProperty("image").GetString()!)))).Equals(c.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase), "fixture hash mismatch");
        var folder = Path.Combine(scratch, "PDF資料"); Directory.CreateDirectory(folder);
        var result = await new ImageToPdfService().ConvertAsync(new(images, Path.Combine(folder, "評価資料.pdf"), ImagePageMode.A4Portrait, false), progress, default);
        Check(!result.HasErrors, "PDF creation failed: " + string.Join("\n", result.Errors));
        suitePdf = Directory.GetFiles(folder, "*.pdf").Single();
        using var document = PdfReader.Open(suitePdf, PdfDocumentOpenMode.Import);
        Check(document.PageCount == 9, "wrong page count");
        File.Copy(suitePdf, Path.Combine(evidence, "evaluation-app.pdf"), true);
    });
    Environment.SetEnvironmentVariable("OMP_THREAD_LIMIT", "4");
    await Test("real application PDF-to-TXT accuracy and time, 9 cases x 3 runs", async () =>
    {
        var engines = File.Exists("/opt/homebrew/bin/tesseract") ? new[] { "paddle", "tesseract" } : new[] { "paddle" };
        foreach (var backend in engines)
        for (var index = 0; index < cases.Length; index++)
        for (var repeat = 0; repeat < 3; repeat++)
        {
            var c = cases[index]; var id = c.GetProperty("id").GetString()!; var language = c.GetProperty("language").GetString()!;
            var output = Path.Combine(evidence, $"{backend}-{id}-{repeat}.txt");
            if (File.Exists(output)) throw new IOException("Use a fresh evidence directory.");
            var watch = Stopwatch.StartNew();
            if (backend == "paddle") await service.ExtractBundledAsync(suitePdf, output, language, (index + 1).ToString(), "", progress, default);
            else await service.ExtractAsync(new(suitePdf, output, "/opt/homebrew/bin/tesseract", Path.Combine(repository, "src/OfflinePDFConverter/ocr/tessdata"), language, (index + 1).ToString()), progress, default);
            var seconds = watch.Elapsed.TotalSeconds;
            var text = Body(await File.ReadAllTextAsync(output)); var truth = Normalize(c.GetProperty("truth").GetString()!);
            var edits = Distance(truth, Normalize(text));
            rows.Add(new { backend, id, repeat, seconds, characters = truth.Length, edits, text });
            File.WriteAllText(Path.Combine(evidence, "accuracy-raw.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"{backend} {id} run {repeat}: {edits}/{truth.Length}, {seconds:F3}s");
        }
    });
    var imagePath = Path.Combine(fixtures, "horizontal_sans.png");
    await Test("cancel in-flight inference and successfully recognize the next page", async () =>
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        var watch = Stopwatch.StartNew();
        try { await PaddleOcrService.RecognizeImageAsync(imagePath, "jpn+eng", cancellation.Token); throw new Exception("cancellation ignored"); }
        catch (OperationCanceledException) { Check(watch.Elapsed < TimeSpan.FromSeconds(5), "slow cancellation"); }
        Check((await PaddleOcrService.RecognizeImageAsync(imagePath, "jpn+eng", default)).Contains("資料"), "recovery after cancellation failed");
    });
    await Test("page timeout is reported and subsequent inference recovers", async () =>
    {
        try { await PaddleOcrService.RecognizeImageAsync(imagePath, "jpn+eng", default, TimeSpan.FromMilliseconds(20)); throw new Exception("timeout ignored"); }
        catch (TimeoutException) { }
        Check((await PaddleOcrService.RecognizeImageAsync(imagePath, "jpn+eng", default)).Contains("資料"), "recovery after timeout failed");
    });
    await Test("invalid image is an error and does not corrupt model state", async () =>
    {
        var bad = Path.Combine(scratch, "broken.png"); File.WriteAllText(bad, "not an image");
        var rejected = false;
        try { await PaddleOcrService.RecognizeImageAsync(bad, "eng", default); } catch (Exception ex) when (ex is not OperationCanceledException) { rejected = true; }
        Check(rejected, "invalid image accepted");
        Check((await PaddleOcrService.RecognizeImageAsync(imagePath, "jpn+eng", default)).Contains("資料"), "recovery after invalid image failed");
    });
    await Test("page selection and atomic output preserve existing files", async () =>
    {
        var folder = Path.Combine(scratch, "selection"); Directory.CreateDirectory(folder);
        var output = Path.Combine(folder, "結果.txt"); File.WriteAllText(output, "keep existing output");
        await service.ExtractBundledAsync(suitePdf, output, "jpn+eng", "2,8", "", progress, default);
        Check(File.ReadAllText(output) == "keep existing output", "overwrote output");
        var created = Directory.GetFiles(folder, "*.txt").Single(p => p != output);
        var text = File.ReadAllText(created);
        Check(Regex.Matches(text, "--- ").Count == 2 && text.Contains("2ページ目") && text.Contains("8ページ目"), "page selection wrong");
        Check(text.Contains("東京都") && text.Contains("Invoice"), "selected page text missing");
        try { await service.ExtractBundledAsync(suitePdf, Path.Combine(folder, "invalid.txt"), "jpn+eng", "99", "", progress, default); throw new Exception("invalid pages accepted"); }
        catch (ArgumentException) { Check(!File.Exists(Path.Combine(folder, "invalid.txt")), "invalid request wrote output"); }
    });
    await Test("password-protected PDF uses the same OCR pipeline", async () =>
    {
        var protectedPdf = Path.Combine(scratch, "暗号化.pdf");
        using (var doc = PdfReader.Open(suitePdf, PdfDocumentOpenMode.Modify))
        { doc.SecuritySettings.UserPassword = "test-password"; doc.SecuritySettings.OwnerPassword = "owner-password"; doc.Save(protectedPdf); }
        var output = Path.Combine(scratch, "protected.txt");
        await service.ExtractBundledAsync(protectedPdf, output, "jpn+eng", "1", "test-password", progress, default);
        Check(File.ReadAllText(output).Contains("日本語"), "encrypted OCR failed");
    });
}
finally { PaddleOcrService.Cleanup(); Directory.Delete(scratch, true); }
File.WriteAllText(Path.Combine(evidence, "checks.json"), JsonSerializer.Serialize(new { passed = failed == 0, checks, failed }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"RESULT {checks.Count - failed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

sealed class SilentProgress : IProgress<ConversionProgress> { public void Report(ConversionProgress value) { } }
