using OfflinePDFConverter.Models;

namespace OfflinePDFConverter.Services;

public interface IPdfToImageService
{
    Task<ConversionResult> ConvertAsync(
        PdfToImageRequest request,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken);
}
