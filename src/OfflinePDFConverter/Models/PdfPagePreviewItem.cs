using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;

namespace OfflinePDFConverter.Models;

public sealed class PdfPagePreviewItem : INotifyPropertyChanged
{
    public PdfPagePreviewItem(
        string pdfPath,
        int pageNumber,
        Bitmap thumbnail,
        double pageWidthPoints,
        double pageHeightPoints,
        byte[] bgraPixels,
        bool isPageSelectionVisible,
        string selectionLabel)
    {
        PdfPath = pdfPath;
        PageNumber = pageNumber;
        Thumbnail = thumbnail;
        PageWidthPoints = pageWidthPoints;
        PageHeightPoints = pageHeightPoints;
        BgraPixels = bgraPixels;
        IsPageSelectionVisible = isPageSelectionVisible;
        SelectionLabel = selectionLabel;
    }

    private bool _hasEditMarker;
    private double _editMarkerLeft;
    private double _editMarkerTop;
    private bool _isPageSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PdfPath { get; }

    public int PageNumber { get; }

    public Bitmap Thumbnail { get; }

    public double PageWidthPoints { get; }

    public double PageHeightPoints { get; }

    public int ThumbnailPixelWidth => Thumbnail.PixelSize.Width;

    public int ThumbnailPixelHeight => Thumbnail.PixelSize.Height;

    public byte[] BgraPixels { get; }

    public bool IsPageSelectionVisible { get; }

    public string SelectionLabel { get; }

    public bool IsPageSelected
    {
        get => _isPageSelected;
        set => SetField(ref _isPageSelected, value);
    }

    public bool HasEditMarker
    {
        get => _hasEditMarker;
        set => SetField(ref _hasEditMarker, value);
    }

    public double EditMarkerLeft
    {
        get => _editMarkerLeft;
        set => SetField(ref _editMarkerLeft, value);
    }

    public double EditMarkerTop
    {
        get => _editMarkerTop;
        set => SetField(ref _editMarkerTop, value);
    }

    public string Title => $"{Path.GetFileName(PdfPath)}";

    public string PageLabel => $"{PageNumber}ページ";

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
