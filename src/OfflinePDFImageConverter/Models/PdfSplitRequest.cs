namespace OfflinePDFImageConverter.Models;

public sealed record PdfSplitRequest(
    IReadOnlyList<string> PdfFiles,
    string OutputFolder);
