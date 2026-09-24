using System.Text;
using OfflinePDFConverter.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace OfflinePDFConverter.Services;

public sealed class PdfTextExtractionService : IPdfTextExtractionService
{
    public Task<ConversionResult> ExtractAsync(
        PdfTextExtractionRequest request,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => Extract(request, progress, cancellationToken), cancellationToken);
    }

    private static ConversionResult Extract(
        PdfTextExtractionRequest request,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken)
    {
        Validate(request);

        var outputPath = FileNameHelper.IncludeSourceNamesInPath(
            EnsureTextExtension(request.OutputTextPath),
            request.PdfFiles);
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var totalPages = GetTotalPageCount(request, cancellationToken);
        var completed = 0;
        var extractedCharacters = 0;
        var builder = new StringBuilder();

        foreach (var pdfPath in request.PdfFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = OpenDocument(pdfPath, GetPassword(request.Passwords, pdfPath));
            var selectedPages = request.SelectedPages.TryGetValue(pdfPath, out var pages)
                ? pages.ToHashSet()
                : new HashSet<int>();
            var textSelections = request.TextSelections
                .Where(item => string.Equals(item.PdfPath, pdfPath, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(item => item.PageNumber, item => item.Text);
            var hasAnySelection = request.SelectedPages.Values.Any(value => value.Count > 0)
                                  || request.TextSelections.Count > 0;

            if (request.PdfFiles.Count > 1)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.AppendLine($"===== {Path.GetFileName(pdfPath)} =====");
            }

            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (hasAnySelection
                    && !selectedPages.Contains(page.Number)
                    && !textSelections.ContainsKey(page.Number))
                {
                    continue;
                }

                var pageText = textSelections.TryGetValue(page.Number, out var selectedText)
                    ? selectedText.Trim()
                    : ContentOrderTextExtractor.GetText(page).Trim();

                builder.AppendLine($"--- {page.Number}ページ目 ---");
                if (pageText.Length == 0)
                {
                    builder.AppendLine("[抽出できる文字情報がありません]");
                }
                else
                {
                    builder.AppendLine(pageText);
                    extractedCharacters += pageText.Length;
                }

                builder.AppendLine();
                completed++;
                progress.Report(new ConversionProgress(
                    completed,
                    totalPages,
                    $"{Path.GetFileName(pdfPath)}: {page.Number}/{document.NumberOfPages}ページのテキストを準備しました"));
            }
        }

        if (extractedCharacters == 0)
        {
            throw new ArgumentException("このPDFには抽出できる文字情報がありません。画像PDFの場合はOCRが必要です。");
        }

        AtomicFile.Write(outputPath, path => File.WriteAllText(path, builder.ToString().TrimEnd() + Environment.NewLine, new UTF8Encoding(false)), cancellationToken);
        return new ConversionResult(1, Array.Empty<string>());
    }

    private static int GetTotalPageCount(PdfTextExtractionRequest request, CancellationToken cancellationToken)
    {
        var totalPages = 0;
        var hasAnySelection = request.SelectedPages.Values.Any(value => value.Count > 0)
                              || request.TextSelections.Count > 0;
        foreach (var pdfPath in request.PdfFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = OpenDocument(pdfPath, GetPassword(request.Passwords, pdfPath));
            if (!hasAnySelection)
            {
                totalPages += document.NumberOfPages;
                continue;
            }

            var selectedPages = request.SelectedPages.TryGetValue(pdfPath, out var pages)
                ? pages
                : Array.Empty<int>();
            var finePages = request.TextSelections
                .Where(item => string.Equals(item.PdfPath, pdfPath, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.PageNumber);
            totalPages += selectedPages.Concat(finePages).Distinct().Count();
        }

        return totalPages;
    }

    private static PdfDocument OpenDocument(string path, string password)
    {
        return string.IsNullOrEmpty(password)
            ? PdfDocument.Open(path)
            : PdfDocument.Open(path, new ParsingOptions { Password = password });
    }

    private static string GetPassword(IReadOnlyDictionary<string, string> passwords, string pdfPath)
    {
        return passwords.TryGetValue(pdfPath, out var password) ? password : string.Empty;
    }

    private static void Validate(PdfTextExtractionRequest request)
    {
        if (request.PdfFiles.Count == 0)
        {
            throw new ArgumentException("テキスト出力するPDFを選択してください。");
        }

        foreach (var pdfFile in request.PdfFiles)
        {
            if (!File.Exists(pdfFile))
            {
                throw new FileNotFoundException("PDFファイルが見つかりません。", pdfFile);
            }

            if (!string.Equals(Path.GetExtension(pdfFile), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("PDFファイルだけを選択してください。");
            }
        }

        if (string.IsNullOrWhiteSpace(request.OutputTextPath))
        {
            throw new ArgumentException("出力するテキストの保存先を指定してください。");
        }
    }

    private static string EnsureTextExtension(string path)
    {
        var trimmed = path.Trim();
        return trimmed.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}.txt";
    }
}
