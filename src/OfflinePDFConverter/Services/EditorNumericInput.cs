namespace OfflinePDFConverter.Services;

/// <summary>Rejects non-finite values before they reach PDF or UI geometry.</summary>
public static class EditorNumericInput
{
    public static double Positive(string? value, string label)
    {
        if (!double.TryParse(value?.Trim(), out var number) || !double.IsFinite(number) || number <= 0)
            throw new ArgumentException($"{label}は0より大きい有限の数字で入力してください。");
        return number;
    }

    public static double NonNegative(string? value, string label)
    {
        if (!double.TryParse(value?.Trim(), out var number) || !double.IsFinite(number) || number < 0)
            throw new ArgumentException($"{label}は0以上の有限の数字で入力してください。");
        return number;
    }
}
