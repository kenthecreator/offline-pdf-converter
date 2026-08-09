namespace OfflinePDFConverter.Models;

public sealed record PdfExtractPagesRequest(
    IReadOnlyList<string> PdfFiles,
    string PagesToExtract,
    string OutputPdfPath);
