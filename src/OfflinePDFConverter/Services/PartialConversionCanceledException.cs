namespace OfflinePDFConverter.Services;

/// <summary>Cancellation preserves the number of files already published atomically.</summary>
public sealed class PartialConversionCanceledException(int createdFiles, CancellationToken token)
    : OperationCanceledException("処理を中止しました。保存済みのファイルは保持します。", token)
{
    public int CreatedFiles { get; } = createdFiles;
}
