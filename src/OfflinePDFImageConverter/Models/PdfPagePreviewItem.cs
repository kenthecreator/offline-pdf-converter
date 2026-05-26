using Avalonia.Media.Imaging;

namespace OfflinePDFImageConverter.Models;

public sealed class PdfPagePreviewItem
{
    public PdfPagePreviewItem(
        string pdfPath,
        int pageNumber,
        Bitmap thumbnail,
        bool isDeleteSelectionVisible)
    {
        PdfPath = pdfPath;
        PageNumber = pageNumber;
        Thumbnail = thumbnail;
        IsDeleteSelectionVisible = isDeleteSelectionVisible;
    }

    public string PdfPath { get; }

    public int PageNumber { get; }

    public Bitmap Thumbnail { get; }

    public bool IsDeleteSelectionVisible { get; }

    public bool IsMarkedForDelete { get; set; }

    public string Title => $"{Path.GetFileName(PdfPath)}";

    public string PageLabel => $"{PageNumber}ページ";
}
