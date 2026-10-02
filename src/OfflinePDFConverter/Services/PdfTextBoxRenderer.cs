using System.Globalization;
using PdfSharp.Drawing;

namespace OfflinePDFConverter.Services;

/// <summary>Wraps editor text, including Japanese without spaces, inside its saved box.</summary>
internal static class PdfTextBoxRenderer
{
    public static void Draw(XGraphics graphics, string text, XFont font, XBrush brush,
        XRect box, string alignment)
    {
        var state = graphics.Save();
        try
        {
            graphics.IntersectClip(box);
            var y = box.Y;
            var lineHeight = font.GetHeight();
            foreach (var line in Wrap(graphics, text, font, box.Width))
            {
                if (y >= box.Bottom) break;
                var width = graphics.MeasureString(line, font).Width;
                var x = alignment switch
                {
                    "Center" => box.X + (box.Width - width) / 2,
                    "Right" => box.Right - width,
                    _ => box.X
                };
                graphics.DrawString(line, font, brush, new XPoint(x, y), XStringFormats.TopLeft);
                y += lineHeight;
            }
        }
        finally { graphics.Restore(state); }
    }

    private static IEnumerable<string> Wrap(XGraphics graphics, string text, XFont font, double width)
    {
        foreach (var paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (paragraph.Length == 0) { yield return string.Empty; continue; }
            // Keep combining characters and supplementary characters together.
            var elements = new List<string>();
            var enumerator = StringInfo.GetTextElementEnumerator(paragraph);
            while (enumerator.MoveNext()) elements.Add(enumerator.GetTextElement());
            for (var start = 0; start < elements.Count;)
            {
                var end = start + 1;
                while (end < elements.Count
                    && graphics.MeasureString(string.Concat(elements.GetRange(start, end - start + 1)), font).Width <= width)
                    end++;
                // Prefer a word boundary for Latin text; Japanese can wrap between characters.
                if (end < elements.Count)
                {
                    for (var index = end - 1; index > start; index--)
                    {
                        if (!string.IsNullOrWhiteSpace(elements[index])) continue;
                        end = index + 1;
                        break;
                    }
                }
                yield return string.Concat(elements.GetRange(start, end - start));
                start = end;
            }
        }
    }
}
