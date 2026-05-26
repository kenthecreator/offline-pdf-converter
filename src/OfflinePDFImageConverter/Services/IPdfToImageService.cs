using OfflinePDFImageConverter.Models;

namespace OfflinePDFImageConverter.Services;

public interface IPdfToImageService
{
    Task<ConversionResult> ConvertAsync(
        PdfToImageRequest request,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken);
}
