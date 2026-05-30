using OfflinePDFConverter.Models;

namespace OfflinePDFConverter.Services;

public interface IImageToPdfService
{
    Task<ConversionResult> ConvertAsync(
        ImageToPdfRequest request,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken);
}
