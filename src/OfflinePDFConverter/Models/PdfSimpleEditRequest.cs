namespace OfflinePDFConverter.Models;

public sealed record PdfSimpleEditRequest(
    IReadOnlyList<string> PdfFiles,
    IReadOnlyList<PdfTextEditItem> Edits,
    string OutputPdfPath);

public sealed record PdfTextEditItem(
    int PageNumber,
    double X,
    double Y,
    double Width,
    double Height,
    string Text,
    string FontFamily,
    double FontSize,
    bool AddWhiteBox,
    string BackgroundColorHex,
    string TextColorHex,
    string TextAlignment);
