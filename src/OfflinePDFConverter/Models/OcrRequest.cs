namespace OfflinePDFConverter.Models;
public sealed record OcrRequest(string PdfPath, string OutputPath, string EnginePath, string DataDirectory,
    string Language, string Pages, string Password = "");
