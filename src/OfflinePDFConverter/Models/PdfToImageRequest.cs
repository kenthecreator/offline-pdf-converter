namespace OfflinePDFConverter.Models;

public sealed record PdfToImageRequest(
    IReadOnlyList<string> PdfFiles,
    string OutputFolder,
    PdfImageFormat OutputFormat,
    int Dpi);
