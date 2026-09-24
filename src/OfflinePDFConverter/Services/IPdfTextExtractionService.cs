using OfflinePDFConverter.Models;

namespace OfflinePDFConverter.Services;

public interface IPdfTextExtractionService
{
    Task<ConversionResult> ExtractAsync(
        PdfTextExtractionRequest request,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken);
}
