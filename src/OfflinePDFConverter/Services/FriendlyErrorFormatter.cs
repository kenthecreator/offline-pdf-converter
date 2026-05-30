using System.IO;

namespace OfflinePDFConverter.Services;

public static class FriendlyErrorFormatter
{
    public static string ToUserMessage(Exception exception)
    {
        return exception switch
        {
            OperationCanceledException => "処理を中止しました。",
            UnauthorizedAccessException => "ファイルまたは保存先にアクセスできません。別の保存先を選ぶか、ファイルを閉じてからもう一度お試しください。",
            DirectoryNotFoundException => "保存先フォルダが見つかりません。保存先を選び直してください。",
            FileNotFoundException => "選択されたファイルが見つかりません。ファイルを選び直してください。",
            IOException => "ファイルを読み書きできません。ほかのアプリで開いていないか確認してください。",
            DllNotFoundException => "変換に必要な部品を読み込めません。配布ファイルを作り直してください。",
            BadImageFormatException => "このファイル形式は読み込めません。JPEG、PNG、PDFのいずれかを選んでください。",
            ArgumentException => string.IsNullOrWhiteSpace(exception.Message)
                ? "選択内容に問題があります。ファイルや保存先を確認してください。"
                : exception.Message,
            _ => "変換できませんでした。ファイルが破損しているか、対応していない形式の可能性があります。"
        };
    }
}
