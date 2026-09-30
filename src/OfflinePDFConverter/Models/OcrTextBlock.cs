namespace OfflinePDFConverter.Models;

/// <summary>Recognized text and its bounds in the OCR input image, in pixels.</summary>
public sealed record OcrTextBlock(string Text, double X, double Y, double Width, double Height);
