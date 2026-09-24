namespace OfflinePDFConverter.Models;

public sealed record PdfSplitRequest(
    IReadOnlyList<string> PdfFiles,
    string OutputFolder,
    string OutputBaseName,
    IReadOnlyDictionary<string, string> Passwords);
