namespace OfflinePDFImageConverter.Models;

public sealed record ConversionResult(int CreatedFiles, IReadOnlyList<string> Errors)
{
    public bool HasErrors => Errors.Count > 0;
}
