using OfflinePDFImageConverter.Models;

namespace OfflinePDFImageConverter.Services;

public interface IPdfDocumentService
{
    Task<ConversionResult> MergeAsync(
        PdfMergeRequest request,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken);

    Task<ConversionResult> SplitAsync(
        PdfSplitRequest request,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken);

    Task<ConversionResult> DeletePagesAsync(
        PdfDeletePagesRequest request,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken);
}
