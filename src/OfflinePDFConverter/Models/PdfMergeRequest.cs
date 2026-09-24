namespace OfflinePDFConverter.Models;

public sealed record PdfMergeRequest(
    IReadOnlyList<string> PdfFiles,
    string OutputPdfPath,
    IReadOnlyDictionary<string, string> Passwords);
