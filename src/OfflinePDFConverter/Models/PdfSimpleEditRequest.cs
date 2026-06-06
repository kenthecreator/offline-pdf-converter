namespace OfflinePDFConverter.Models;

public sealed record PdfSimpleEditRequest(
    IReadOnlyList<string> PdfFiles,
    IReadOnlyList<PdfTextEditItem> Edits,
    IReadOnlyList<PdfShapeEditItem> Shapes,
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
    string TextAlignment,
    bool IsBold,
    bool IsUnderline);

public sealed record PdfShapeEditItem(
    int PageNumber,
    double X,
    double Y,
    double Width,
    double Height,
    string ShapeType,
    string FillColorHex,
    string StrokeColorHex,
    double StrokeThickness,
    double CornerRadius,
    double RotationDegrees);
