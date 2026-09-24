using Avalonia.Controls;
namespace OfflinePDFConverter.Views;
public partial class MainWindow
{
    private void OnOutputPresetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || _pdfFormatCombo == null || _pdfDpiCurrentHint == null) return;
        switch (combo.SelectedIndex)
        {
            case 0: _pdfFormatCombo.SelectedIndex = 0; SetPdfDpiIndex(1); break;
            case 1: _pdfFormatCombo.SelectedIndex = 1; SetPdfDpiIndex(0); break;
            case 2: _pdfFormatCombo.SelectedIndex = 0; SetPdfDpiIndex(2); break;
        }
        if (this.FindControl<NumericUpDown>("JpegQualityInput") is { } quality)
            quality.Value = combo.SelectedIndex == 1 ? 80 : 95;
    }
}
