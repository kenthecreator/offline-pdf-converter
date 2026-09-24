using Avalonia;
using Avalonia.Input;

namespace OfflinePDFConverter.Models;

/// <summary>Maps a pointer gesture to characters in the PDF's existing text layer.</summary>
public static class PdfTextSelectionLogic
{
    private const double DragThresholdPixels = 5;

    public static bool IsAdditiveModifier(KeyModifiers modifiers, bool isMac)
        => modifiers.HasFlag(isMac ? KeyModifiers.Meta : KeyModifiers.Control);

    public static bool IsDrag(Point start, Point current, double zoomScale)
    {
        var dx = (current.X - start.X) * zoomScale;
        var dy = (current.Y - start.Y) * zoomScale;
        return dx * dx + dy * dy >= DragThresholdPixels * DragThresholdPixels;
    }

    public static int? HitCharacter(IReadOnlyList<PdfSelectableWord> characters, Point point)
    {
        var index = FindCharacter(characters, point);
        if (index is not int value) return null;
        var character = characters.First(item => item.Index == value);
        return new Rect(character.Left - 8, character.Top - 8,
            character.Width + 16, character.Height + 16).Contains(point) ? value : null;
    }

    public static int? FindCharacter(IReadOnlyList<PdfSelectableWord> characters, Point point)
    {
        if (characters.Count == 0) return null;
        var lineCharacter = characters.MinBy(character =>
            Math.Max(0, Math.Abs(point.Y - (character.Top + character.Height / 2)) - character.Height / 2));
        if (lineCharacter == null) return null;
        return characters.Where(character => OnSameLine(lineCharacter, character))
            .MinBy(character => Math.Max(0,
                Math.Abs(point.X - (character.Left + character.Width / 2)) - character.Width / 2))?.Index;
    }

    public static IReadOnlyList<int> SelectRange(IReadOnlyList<PdfSelectableWord> characters, int start, int end)
        => characters.Where(character => character.Index >= Math.Min(start, end)
                                      && character.Index <= Math.Max(start, end))
            .Select(character => character.Index).ToArray();

    public static IReadOnlyList<int> ApplyGesture(
        IReadOnlyList<PdfSelectableWord> characters,
        IReadOnlyCollection<int> previous,
        int? pressedIndex,
        int? endIndex,
        int? previousAnchor,
        bool dragged,
        bool additive,
        bool extend)
    {
        if (!dragged) return additive || extend ? previous.Order().ToArray() : Array.Empty<int>();
        if (pressedIndex is not int start)
            return additive || extend ? previous.Order().ToArray() : Array.Empty<int>();

        var selection = extend && previousAnchor is int anchor
            ? SelectRange(characters, anchor, endIndex ?? start)
            : SelectRange(characters, start, endIndex ?? start);
        return (additive ? previous.Concat(selection) : selection)
            .Distinct().Order().ToArray();
    }

    private static bool OnSameLine(PdfSelectableWord first, PdfSelectableWord second)
    {
        var firstHeight = Math.Max(first.Height, 4);
        var secondHeight = Math.Max(second.Height, 4);
        var overlap = Math.Max(0, Math.Min(first.Top + firstHeight, second.Top + secondHeight)
                                  - Math.Max(first.Top, second.Top));
        if (overlap >= Math.Min(firstHeight, secondHeight) * 0.35) return true;
        return Math.Abs(first.Top + firstHeight / 2 - second.Top - secondHeight / 2)
               <= Math.Max(firstHeight, secondHeight) * 0.55;
    }
}
