namespace OfflinePDFImageConverter.Models;

public sealed record PdfDeletePagesRequest(
    IReadOnlyList<string> PdfFiles,
    string PagesToDelete,
    string OutputPdfPath);
