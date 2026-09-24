using OfflinePDFConverter.Models;
namespace OfflinePDFConverter.Services;

public static class BatchRunner
{
    public static async Task<ConversionResult> RunAsync(IReadOnlyList<string> files,
        Func<string, IProgress<ConversionProgress>, CancellationToken, Task<ConversionResult>> convert,
        IProgress<ConversionProgress> progress, CancellationToken token)
    {
        if (files.Count == 0) throw new ArgumentException("処理するファイルを選択してください。");
        var items = new List<FileConversionResult>();
        var errors = new List<string>();
        var created = 0;
        foreach (var file in files)
        {
            if (token.IsCancellationRequested)
            {
                items.Add(new(file, FileConversionStatus.NotStarted, 0, "未処理"));
                continue;
            }
            try
            {
                var done = items.Count;
                var fileProgress = new InlineProgress(value => progress.Report(new ConversionProgress(
                    done * 100 + (int)Math.Clamp(value.Percent, 0, 100), files.Count * 100, value.Message)));
                var result = await convert(file, fileProgress, token);
                created += result.CreatedFiles;
                errors.AddRange(result.Errors);
                items.Add(new(file, result.HasErrors ? FileConversionStatus.Failed : FileConversionStatus.Succeeded,
                    result.CreatedFiles, string.Join("\n", result.Errors)));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { items.Add(new(file, FileConversionStatus.Cancelled, 0, "中止（保存済みのページは残ります）")); }
            catch (Exception ex)
            {
                var message = FriendlyErrorFormatter.ToUserMessage(ex);
                errors.Add($"{Path.GetFileName(file)}: {message}");
                items.Add(new(file, FileConversionStatus.Failed, 0, message));
            }
        }
        return new(created, errors) { Items = items };
    }
    private sealed class InlineProgress(Action<ConversionProgress> report) : IProgress<ConversionProgress>
    { public void Report(ConversionProgress value) => report(value); }
}
