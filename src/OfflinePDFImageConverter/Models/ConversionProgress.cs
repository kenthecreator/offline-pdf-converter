namespace OfflinePDFImageConverter.Models;

public sealed record ConversionProgress(int Completed, int Total, string Message)
{
    public double Percent => Total <= 0 ? 0 : Math.Clamp(Completed * 100d / Total, 0, 100);
}
