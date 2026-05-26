namespace OfflinePDFImageConverter.Models;

public sealed record PdfMergeRequest(
    IReadOnlyList<string> PdfFiles,
    string OutputPdfPath);
