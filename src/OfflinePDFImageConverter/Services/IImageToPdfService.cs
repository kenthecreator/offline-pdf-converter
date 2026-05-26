using OfflinePDFImageConverter.Models;

namespace OfflinePDFImageConverter.Services;

public interface IImageToPdfService
{
    Task<ConversionResult> ConvertAsync(
        ImageToPdfRequest request,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken);
}
