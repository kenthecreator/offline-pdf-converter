#if PADDLE_OCR
using RapidOcrNet;
using Microsoft.ML.OnnxRuntime;
using OfflinePDFConverter.Models;

namespace OfflinePDFConverter.Services;

/// <summary>Local CPU inference; one cached model set and one cancellable inference at a time.</summary>
public static class PaddleOcrService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Dictionary<string, RapidOcr> Engines = new();
    private static BundledPaddleOcrRuntime? runtime;

    public static async Task<string> RecognizeImageAsync(string imagePath, string language,
        CancellationToken token, TimeSpan? pageTimeout = null)
        => string.Join("\n", (await RecognizeBlocksAsync(imagePath, language, token, pageTimeout)).Select(block => block.Text));

    public static async Task<IReadOnlyList<OcrTextBlock>> RecognizeBlocksAsync(string imagePath, string language,
        CancellationToken token, TimeSpan? pageTimeout = null)
    {
        if (language is not ("jpn" or "jpn_vert" or "jpn+eng" or "jpn_vert+eng" or "eng")) throw new ArgumentException("認識言語を選択してください。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(pageTimeout ?? TimeSpan.FromMinutes(3));
        var entered = false;
        try
        {
            await Gate.WaitAsync(timeout.Token);
            entered = true;
            var engine = await Task.Run(() => EnsureEngine(language, timeout.Token), timeout.Token);
            var result = await engine.DetectAsync(imagePath, RapidOcrOptions.PPOCRv6, null, timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            // Vertical Japanese reads down a column and then from right to left.
            // Sorting uses detected geometry only; it never uses expected text.
            var blocks = language.StartsWith("jpn_vert", StringComparison.Ordinal)
                ? result.TextBlocks.OrderByDescending(block => block.BoxPoints.Average(point => point.X))
                    .ThenBy(block => block.BoxPoints.Min(point => point.Y))
                : OrderHorizontalBlocks(result.TextBlocks);
            return blocks.Select(block => new OcrTextBlock(block.Text,
                block.BoxPoints.Min(p => (double)p.X), block.BoxPoints.Min(p => (double)p.Y),
                block.BoxPoints.Max(p => (double)p.X) - block.BoxPoints.Min(p => (double)p.X),
                block.BoxPoints.Max(p => (double)p.Y) - block.BoxPoints.Min(p => (double)p.Y))).ToArray();
        }
        catch (OperationCanceledException)
        {
            token.ThrowIfCancellationRequested();
            throw new TimeoutException("1ページの文字認識が制限時間を超えたため中止しました。");
        }
        finally { if (entered) Gate.Release(); }
    }

    private static IEnumerable<TextBlock> OrderHorizontalBlocks(IEnumerable<TextBlock> blocks)
    {
        var byX = blocks.OrderBy(block => block.BoxPoints.Average(point => point.X)).ToArray();
        // Split only two groups separated by a full vertical gutter. A heading or
        // overlapping boxes keep the detector's normal line order.
        if (byX.Length < 4) return blocks;
        var split = Enumerable.Range(2, byX.Length - 3)
            .OrderByDescending(index => byX[index].BoxPoints.Average(p => p.X)
                - byX[index - 1].BoxPoints.Average(p => p.X)).First();
        var left = byX.Take(split).ToArray();
        var right = byX.Skip(split).ToArray();
        var gutter = right.Min(b => b.BoxPoints.Min(p => p.X)) - left.Max(b => b.BoxPoints.Max(p => p.X));
        var widths = byX.Select(b => b.BoxPoints.Max(p => p.X) - b.BoxPoints.Min(p => p.X)).Order().ToArray();
        if (gutter < Math.Max(20, widths[widths.Length / 2] * 0.25)) return blocks;
        return left.OrderBy(b => b.BoxPoints.Min(p => p.Y)).Concat(right.OrderBy(b => b.BoxPoints.Min(p => p.Y)));
    }

    private static RapidOcr EnsureEngine(string language, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var profile = language is "jpn" or "jpn_vert" ? "jpn" : language == "eng" ? "eng" : "mixed";
        if (Engines.TryGetValue(profile, out var cached)) return cached;
        try { OrtEnv.Instance().DisableTelemetryEvents(); }
        catch (Exception ex) when (OperatingSystem.IsWindows() && ex is DllNotFoundException or TypeInitializationException)
        {
            throw new PaddleOcrDependencyException("PaddleOCRの実行部品を読み込めません。Microsoft公式のVisual C++ v14（x64）ランタイムを確認し、アプリを再起動してください。", ex);
        }
        var package = runtime ??= BundledPaddleOcrRuntime.ExtractEmbedded(token);
        var candidate = new RapidOcr();
        try
        {
            using var options = RapidOcr.GetDefaultSessionOptions(4);
            options.InterOpNumThreads = 1;
            options.IntraOpNumThreads = 4;
            var root = package.RootDirectory;
            candidate.InitModels(RapidOcrModelSet.PPOCRv6Small with
            {
                DetModelPath = Path.Combine(root, "det.onnx"),
                ClsModelPath = Path.Combine(root, "cls.onnx"),
                RecModelPath = Path.Combine(root, profile == "mixed" ? "rec.onnx" : $"rec-{profile}.onnx"),
                KeysPath = Path.Combine(root, profile == "mixed" ? "keys.txt" : $"keys-{profile}.txt")
            }, options);
            token.ThrowIfCancellationRequested();
            Engines.Add(profile, candidate);
            return candidate;
        }
        catch { candidate.Dispose(); throw; }
    }

    public static void Cleanup()
    {
        Gate.Wait();
        try
        {
            foreach (var engine in Engines.Values) engine.Dispose();
            Engines.Clear();
            runtime?.Dispose(); runtime = null;
        }
        finally { Gate.Release(); }
    }
}

public sealed class PaddleOcrDependencyException(string message, Exception inner) : Exception(message, inner);
#endif
