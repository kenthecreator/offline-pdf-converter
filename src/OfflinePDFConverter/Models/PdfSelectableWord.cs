namespace OfflinePDFConverter.Models;

public sealed record PdfSelectableWord(
    int Index,
    int WordIndex,
    string Text,
    double Left,
    double Top,
    double Width,
    double Height);

public sealed record PdfTextSelectionResult(
    IReadOnlyList<int> SelectedWordIndices,
    string Text);
