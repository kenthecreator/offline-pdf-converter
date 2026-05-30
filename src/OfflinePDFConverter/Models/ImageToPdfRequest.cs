namespace OfflinePDFConverter.Models;

public sealed record ImageToPdfRequest(
    IReadOnlyList<string> ImageFiles,
    string OutputPdfPath,
    ImagePageMode PageMode,
    bool UseMargin);
