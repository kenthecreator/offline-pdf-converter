namespace OfflinePDFConverter.Services;

public static class PageRangeParser
{
    public static SortedSet<int> Parse(
        string value,
        int pageCount,
        string pageAction = "削除する")
    {
        if (pageCount < 1)
        {
            throw new ArgumentException("PDFにページがありません。");
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return new SortedSet<int>();
        }

        var pages = new SortedSet<int>();
        var parts = value
            .Replace('，', ',')
            .Replace('、', ',')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            if (part.Contains('-', StringComparison.Ordinal))
            {
                var range = part.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (range.Length != 2
                    || !int.TryParse(range[0], out var start)
                    || !int.TryParse(range[1], out var end))
                {
                    throw new ArgumentException($"{pageAction}ページは 1,3,5-7 のように入力してください。");
                }

                if (start > end)
                {
                    (start, end) = (end, start);
                }

                AddRange(pages, start, end, pageCount, pageAction);
            }
            else
            {
                if (!int.TryParse(part, out var page))
                {
                    throw new ArgumentException($"{pageAction}ページは 1,3,5-7 のように入力してください。");
                }

                AddPage(pages, page, pageCount, pageAction);
            }
        }

        return pages;
    }

    private static void AddRange(
        SortedSet<int> pages,
        int start,
        int end,
        int pageCount,
        string pageAction)
    {
        for (var page = start; page <= end; page++)
        {
            AddPage(pages, page, pageCount, pageAction);
        }
    }

    private static void AddPage(
        SortedSet<int> pages,
        int page,
        int pageCount,
        string pageAction)
    {
        if (page < 1 || page > pageCount)
        {
            throw new ArgumentException($"{pageAction}ページは1から{pageCount}までの範囲で指定してください。");
        }

        pages.Add(page);
    }
}
