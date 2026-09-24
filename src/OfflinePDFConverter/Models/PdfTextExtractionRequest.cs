namespace OfflinePDFConverter.Models;

public sealed record PdfTextExtractionRequest(
    IReadOnlyList<string> PdfFiles,
    string OutputTextPath,
    IReadOnlyDictionary<string, string> Passwords,
    IReadOnlyDictionary<string, IReadOnlyList<int>> SelectedPages,
    IReadOnlyList<PdfTextSelectionItem> TextSelections);

public sealed record PdfTextSelectionItem(
    string PdfPath,
    int PageNumber,
    string Text);
