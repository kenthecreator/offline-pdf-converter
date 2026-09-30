namespace OfflinePDFConverter.Models;

public sealed record ConversionResult(int CreatedFiles, IReadOnlyList<string> Errors)
{
    public IReadOnlyList<FileConversionResult> Items { get; init; } = Array.Empty<FileConversionResult>();

    public IReadOnlyList<string> OutputPaths { get; init; } = Array.Empty<string>();

    public bool HasErrors => Errors.Count > 0;
}

public enum FileConversionStatus { Succeeded, Failed, Cancelled, NotStarted }
public sealed record FileConversionResult(string SourcePath, FileConversionStatus Status, int CreatedFiles, string Message);
