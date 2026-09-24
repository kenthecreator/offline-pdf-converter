namespace OfflinePDFConverter.Models;

public sealed record PdfToImageRequest(
    IReadOnlyList<string> PdfFiles,
    string OutputFolder,
    string OutputBaseName,
    PdfImageFormat OutputFormat,
    int Dpi,
    string PagesToConvert,
    IReadOnlyDictionary<string, string> Passwords,
    int JpegQuality = 95);
