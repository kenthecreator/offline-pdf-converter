using System.IO;
using OfflinePDFConverter.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace OfflinePDFConverter.Services;

public sealed class PdfDocumentService : IPdfDocumentService
{
    public Task<ConversionResult> MergeAsync(
        PdfMergeRequest request,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => Merge(request, progress, cancellationToken), cancellationToken);
    }

    public Task<ConversionResult> SplitAsync(
        PdfSplitRequest request,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => Split(request, progress, cancellationToken), cancellationToken);
    }

    public Task<ConversionResult> DeletePagesAsync(
        PdfDeletePagesRequest request,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => DeletePages(request, progress, cancellationToken), cancellationToken);
    }

    public Task<ConversionResult> SimpleEditAsync(
        PdfSimpleEditRequest request,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => SimpleEdit(request, progress, cancellationToken), cancellationToken);
    }

    private static ConversionResult Merge(
        PdfMergeRequest request,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken)
    {
        ValidatePdfFiles(request.PdfFiles);
        if (request.PdfFiles.Count < 2)
        {
            throw new ArgumentException("結合するPDFを2つ以上選択してください。");
        }

        var outputPath = EnsurePdfExtension(request.OutputPdfPath);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("結合後のPDF名と保存先を指定してください。");
        }

        EnsureOutputDoesNotOverwriteSource(outputPath, request.PdfFiles);
        CreateOutputDirectory(outputPath);

        using var output = new PdfDocument();
        output.Info.Title = Path.GetFileNameWithoutExtension(outputPath);
        output.Info.Creator = "Offline PDF Converter";

        var totalPages = request.PdfFiles.Sum(GetPageCount);
        var completed = 0;

        foreach (var pdfPath in request.PdfFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var input = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
            for (var pageIndex = 0; pageIndex < input.PageCount; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.AddPage(input.Pages[pageIndex]);
                completed++;
                progress.Report(new ConversionProgress(
                    completed,
                    totalPages,
                    $"{Path.GetFileName(pdfPath)}: {pageIndex + 1}/{input.PageCount}ページを追加しました"));
            }
        }

        output.Save(outputPath);
        return new ConversionResult(1, Array.Empty<string>());
    }

    private static ConversionResult Split(
        PdfSplitRequest request,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken)
    {
        ValidatePdfFiles(request.PdfFiles);
        if (string.IsNullOrWhiteSpace(request.OutputFolder))
        {
            throw new ArgumentException("分割したPDFの保存先フォルダを選択してください。");
        }

        Directory.CreateDirectory(request.OutputFolder);

        var totalPages = request.PdfFiles.Sum(GetPageCount);
        var completed = 0;
        var createdFiles = 0;
        var errors = new List<string>();

        for (var pdfFileIndex = 0; pdfFileIndex < request.PdfFiles.Count; pdfFileIndex++)
        {
            var pdfPath = request.PdfFiles[pdfFileIndex];
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var input = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
                var digits = Math.Max(3, input.PageCount.ToString().Length);
                var baseName = GetOutputBaseName(request.OutputBaseName, pdfPath, request.PdfFiles.Count, pdfFileIndex);

                for (var pageIndex = 0; pageIndex < input.PageCount; pageIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var output = new PdfDocument();
                    output.Info.Title = $"{baseName}_page{pageIndex + 1}";
                    output.Info.Creator = "Offline PDF Converter";
                    output.AddPage(input.Pages[pageIndex]);

                    var pageNumber = (pageIndex + 1).ToString($"D{digits}");
                    var desiredPath = Path.Combine(request.OutputFolder, $"{baseName}_page{pageNumber}.pdf");
                    output.Save(FileNameHelper.GetUniquePath(desiredPath));

                    completed++;
                    createdFiles++;
                    progress.Report(new ConversionProgress(
                        completed,
                        totalPages,
                        $"{Path.GetFileName(pdfPath)}: {pageIndex + 1}/{input.PageCount}ページを保存しました"));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(pdfPath)}: {FriendlyErrorFormatter.ToUserMessage(ex)}");
            }
        }

        if (createdFiles == 0)
        {
            throw new ArgumentException("分割できるPDFがありませんでした。");
        }

        return new ConversionResult(createdFiles, errors);
    }

    private static ConversionResult DeletePages(
        PdfDeletePagesRequest request,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken)
    {
        ValidatePdfFiles(request.PdfFiles);
        if (request.PdfFiles.Count != 1)
        {
            throw new ArgumentException("ページ削除ではPDFを1つだけ選択してください。");
        }

        var outputPath = EnsurePdfExtension(request.OutputPdfPath);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("ページ削除後のPDF名と保存先を指定してください。");
        }

        EnsureOutputDoesNotOverwriteSource(outputPath, request.PdfFiles);
        CreateOutputDirectory(outputPath);

        var pdfPath = request.PdfFiles[0];
        using var input = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        var pagesToDelete = PageRangeParser.Parse(request.PagesToDelete, input.PageCount);

        if (pagesToDelete.Count == 0)
        {
            throw new ArgumentException("削除するページを入力してください。例: 1,3,5-7");
        }

        if (pagesToDelete.Count >= input.PageCount)
        {
            throw new ArgumentException("すべてのページは削除できません。少なくとも1ページは残してください。");
        }

        using var output = new PdfDocument();
        output.Info.Title = Path.GetFileNameWithoutExtension(outputPath);
        output.Info.Creator = "Offline PDF Converter";

        var completed = 0;
        for (var pageIndex = 0; pageIndex < input.PageCount; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageNumber = pageIndex + 1;
            if (!pagesToDelete.Contains(pageNumber))
            {
                output.AddPage(input.Pages[pageIndex]);
            }

            completed++;
            progress.Report(new ConversionProgress(
                completed,
                input.PageCount,
                $"{pageNumber}/{input.PageCount}ページを確認しました"));
        }

        output.Save(outputPath);
        return new ConversionResult(1, Array.Empty<string>());
    }

    private static ConversionResult SimpleEdit(
        PdfSimpleEditRequest request,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken)
    {
        ValidatePdfFiles(request.PdfFiles);
        if (request.PdfFiles.Count != 1)
        {
            throw new ArgumentException("テキスト追加ではPDFを1つだけ選択してください。");
        }

        var outputPath = EnsurePdfExtension(request.OutputPdfPath);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("編集後のPDF名と保存先を指定してください。");
        }

        EnsureOutputDoesNotOverwriteSource(outputPath, request.PdfFiles);
        CreateOutputDirectory(outputPath);

        if (request.Edits.Count == 0)
        {
            throw new ArgumentException("追加するテキストボックスを1つ以上指定してください。");
        }

        foreach (var edit in request.Edits)
        {
            if (edit.PageNumber <= 0)
            {
                throw new ArgumentException("編集するページ番号を1以上で入力してください。");
            }

            if (edit.Width <= 0 || edit.Height <= 0)
            {
                throw new ArgumentException("白塗り範囲の幅と高さは1以上で入力してください。");
            }

            if (edit.FontSize <= 0)
            {
                throw new ArgumentException("文字サイズは1以上で入力してください。");
            }
        }

        var pdfPath = request.PdfFiles[0];
        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var maxPage = request.Edits.Max(edit => edit.PageNumber);
        if (maxPage > document.PageCount)
        {
            throw new ArgumentException($"編集するページ番号は1から{document.PageCount}の範囲で入力してください。");
        }

        document.Info.Title = Path.GetFileNameWithoutExtension(outputPath);
        document.Info.Creator = "Offline PDF Converter";

        for (var pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = document.Pages[pageIndex];
            var pageNumber = pageIndex + 1;

            var pageEdits = request.Edits
                .Where(edit => edit.PageNumber == pageNumber)
                .ToList();
            if (pageEdits.Count > 0)
            {
                using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                foreach (var edit in pageEdits)
                {
                    if (edit.AddWhiteBox)
                    {
                        graphics.DrawRectangle(
                            new XSolidBrush(ToPdfColor(edit.BackgroundColorHex)),
                            edit.X,
                            edit.Y,
                            edit.Width,
                            edit.Height);
                    }

                    if (!string.IsNullOrWhiteSpace(edit.Text))
                    {
                        var fontOptions = new XPdfFontOptions(PdfFontEncoding.Unicode, PdfFontEmbedding.EmbedCompleteFontFile);
                        var font = new XFont(
                            string.IsNullOrWhiteSpace(edit.FontFamily)
                                ? "OfflinePDFConverterGothic"
                                : edit.FontFamily,
                            edit.FontSize,
                            XFontStyleEx.Regular,
                            fontOptions);
                        var format = new XStringFormat
                        {
                            Alignment = edit.TextAlignment switch
                            {
                                "Center" => XStringAlignment.Center,
                                "Right" => XStringAlignment.Far,
                                _ => XStringAlignment.Near
                            },
                            LineAlignment = XLineAlignment.Near
                        };
                        graphics.DrawString(
                            edit.Text,
                            font,
                            new XSolidBrush(ToPdfColor(edit.TextColorHex)),
                            new XRect(edit.X, edit.Y, edit.Width, edit.Height),
                            format);
                    }
                }
            }

            progress.Report(new ConversionProgress(
                pageNumber,
                document.PageCount,
                $"{pageNumber}/{document.PageCount}ページを確認しました"));
        }

        document.Save(outputPath);
        return new ConversionResult(1, Array.Empty<string>());
    }

    private static XColor ToPdfColor(string colorHex)
    {
        if (colorHex.Length == 7
            && colorHex[0] == '#'
            && byte.TryParse(colorHex.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var r)
            && byte.TryParse(colorHex.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)
            && byte.TryParse(colorHex.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return XColor.FromArgb(r, g, b);
        }

        return XColors.White;
    }

    private static int GetPageCount(string pdfPath)
    {
        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    private static string GetOutputBaseName(string requestedBaseName, string pdfPath, int pdfCount, int pdfIndex)
    {
        var baseName = string.IsNullOrWhiteSpace(requestedBaseName)
            ? FileNameHelper.SafeBaseName(pdfPath)
            : FileNameHelper.SafeBaseName(requestedBaseName);

        return pdfCount <= 1 ? baseName : $"{baseName}_pdf{pdfIndex + 1:D3}";
    }

    private static void ValidatePdfFiles(IReadOnlyList<string> pdfFiles)
    {
        if (pdfFiles.Count == 0)
        {
            throw new ArgumentException("PDFファイルを選択してください。");
        }

        foreach (var pdfFile in pdfFiles)
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
    }

    private static string EnsurePdfExtension(string path)
    {
        var trimmed = path.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        return trimmed.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}.pdf";
    }

    private static void CreateOutputDirectory(string outputPath)
    {
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }
    }

    private static void EnsureOutputDoesNotOverwriteSource(string outputPath, IReadOnlyList<string> sourceFiles)
    {
        var fullOutputPath = Path.GetFullPath(outputPath);
        foreach (var sourceFile in sourceFiles)
        {
            if (string.Equals(fullOutputPath, Path.GetFullPath(sourceFile), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("元のPDFと同じ場所には保存できません。別のファイル名を指定してください。");
            }
        }
    }
}
