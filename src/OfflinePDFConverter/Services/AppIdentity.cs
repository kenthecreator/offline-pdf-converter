namespace OfflinePDFConverter.Services;

public static class AppIdentity
{
#if PADDLE_OCR
    public const string WindowTitle = "Offline PDF Converter (v4.4)";
    public const string HeaderSuffix = " (v4.4)";
    public static string[] OcrLanguages => new[] { "日本語（横書き）", "日本語（縦書き）", "英語", "日本語・英語 混在" };
    public static string OcrDescription => "選択した言語で文字を認識し、検索・選択・コピーできるPDFを作成します。処理はこの端末内で完結します。"
        + " 必要な実行部品と認識モデルを内蔵しており、追加インストールやダウンロードは不要です。";
    public static string ReadOcrLicenses() => BundledPaddleOcrRuntime.ReadLicenses();
#else
    public const string WindowTitle = "Offline PDF Converter (v4.4)";
    public const string HeaderSuffix = " (v4.4)";
    public static string[] OcrLanguages => new[] { "日本語（横書き）", "日本語（縦書き）", "英語", "日本語・英語 混在" };
    public static string OcrDescription => OperatingSystem.IsMacOS()
        ? "Macではインストール済みのTesseractとアプリ内の日英認識データを使用します。認識はローカルで行われます。"
        : "文字認識機能はアプリに内蔵されています。追加インストール・ダウンロード・外部送信はありません。";
    public static string ReadOcrLicenses()
    {
        using var package = BundledOcrRuntime.ExtractEmbedded();
        return string.Join("\n\n", Directory.GetFiles(Path.Combine(package.RootDirectory, "licenses")).OrderBy(x => x)
            .Select(path => Path.GetFileName(path) + "\n\n" + File.ReadAllText(path)));
    }
#endif
}
