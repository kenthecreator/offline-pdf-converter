using PdfSharp.Fonts;
using System.Collections.Concurrent;

namespace OfflinePDFConverter.Services;

public sealed class AppFontResolver : IFontResolver
{
    private const string SystemFacePrefix = "OfflinePDFConverter-SystemFont-";
    private const string GothicFaceName = "OfflinePDFConverter-Gothic";
    private const string GothicBoldFaceName = "OfflinePDFConverter-GothicBold";
    private const string MinchoFaceName = "OfflinePDFConverter-Mincho";
    private const string RoundedFaceName = "OfflinePDFConverter-Rounded";
    private const string LatinFaceName = "OfflinePDFConverter-Latin";
    private static readonly ConcurrentDictionary<string, string> SystemFontFamilies = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<IReadOnlyList<string>> SystemFontFiles = new(
        () => SystemFontDirectories()
            .Where(Directory.Exists)
            .SelectMany(directory => EnumerateFontFilesSafely(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList());

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        var normalized = familyName.Trim();

        if (normalized.Contains("Mincho", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("明朝", StringComparison.Ordinal))
        {
            return new FontResolverInfo(MinchoFaceName);
        }

        if (normalized.Contains("Rounded", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("丸", StringComparison.Ordinal))
        {
            return new FontResolverInfo(RoundedFaceName);
        }

        if (normalized.Contains("Latin", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Arial", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("英数字", StringComparison.Ordinal))
        {
            return new FontResolverInfo(LatinFaceName);
        }

        if (!normalized.StartsWith("OfflinePDFConverter", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(normalized))
        {
            var faceName = SystemFacePrefix + NormalizeFontName(normalized);
            SystemFontFamilies[faceName] = normalized;
            return new FontResolverInfo(faceName);
        }

        return new FontResolverInfo(bold ? GothicBoldFaceName : GothicFaceName);
    }

    public byte[]? GetFont(string faceName)
    {
        var path = SystemFontFamilies.TryGetValue(faceName, out var systemFamilyName)
            ? FindSystemFontPath(systemFamilyName)
            : faceName switch
        {
            GothicBoldFaceName => FindFirstExistingPath(GothicBoldFontCandidates()),
            MinchoFaceName => FindFirstExistingPath(MinchoFontCandidates()),
            RoundedFaceName => FindFirstExistingPath(RoundedFontCandidates()),
            LatinFaceName => FindFirstExistingPath(LatinFontCandidates()),
            _ => FindFirstExistingPath(GothicFontCandidates())
        };

        if (path == null && faceName == GothicBoldFaceName)
        {
            path = FindFirstExistingPath(GothicFontCandidates());
        }

        path ??= FindFirstExistingPath(GothicFontCandidates());
        return path == null ? null : File.ReadAllBytes(path);
    }

    private static string? FindSystemFontPath(string familyName)
    {
        var normalizedFamily = NormalizeFontName(familyName);
        if (string.IsNullOrWhiteSpace(normalizedFamily))
        {
            return null;
        }

        if (KnownSystemFontCandidates(familyName).FirstOrDefault(File.Exists) is { } knownPath)
        {
            return knownPath;
        }

        return SystemFontFiles.Value.FirstOrDefault(path => NormalizeFontName(Path.GetFileNameWithoutExtension(path)) == normalizedFamily)
            ?? SystemFontFiles.Value.FirstOrDefault(path => NormalizeFontName(Path.GetFileNameWithoutExtension(path)).Contains(normalizedFamily, StringComparison.OrdinalIgnoreCase))
            ?? SystemFontFiles.Value.FirstOrDefault(path => normalizedFamily.Contains(NormalizeFontName(Path.GetFileNameWithoutExtension(path)), StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeFontName(string value)
    {
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static IEnumerable<string> EnumerateFontFilesSafely(string directory)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file);
            if (extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".ttc", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".otf", StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> SystemFontDirectories()
    {
        yield return "/System/Library/Fonts";
        yield return "/System/Library/Fonts/Supplemental";
        yield return "/Library/Fonts";
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Fonts");
        yield return @"C:\Windows\Fonts";
        yield return @"/usr/share/fonts";
        yield return @"/usr/local/share/fonts";
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local",
            "share",
            "fonts");
    }

    private static IEnumerable<string> KnownSystemFontCandidates(string familyName)
    {
        if (familyName.Contains("Hiragino Sans", StringComparison.OrdinalIgnoreCase)
            || familyName.Contains("ヒラギノ角", StringComparison.Ordinal))
        {
            yield return "/System/Library/Fonts/ヒラギノ角ゴシック W3.ttc";
            yield return "/System/Library/Fonts/ヒラギノ角ゴシック W4.ttc";
            yield return "/System/Library/Fonts/ヒラギノ角ゴシック W6.ttc";
        }

        if (familyName.Contains("Hiragino Mincho", StringComparison.OrdinalIgnoreCase)
            || familyName.Contains("ヒラギノ明", StringComparison.Ordinal))
        {
            yield return "/System/Library/Fonts/ヒラギノ明朝 ProN.ttc";
            yield return "/System/Library/Fonts/ヒラギノ明朝 Pro.ttc";
        }

        if (familyName.Contains("Yu Gothic", StringComparison.OrdinalIgnoreCase)
            || familyName.Contains("游ゴシック", StringComparison.Ordinal))
        {
            yield return @"C:\Windows\Fonts\YuGothR.ttc";
            yield return @"C:\Windows\Fonts\YuGothM.ttc";
            yield return @"C:\Windows\Fonts\YuGothB.ttc";
        }

        if (familyName.Contains("Yu Mincho", StringComparison.OrdinalIgnoreCase)
            || familyName.Contains("游明朝", StringComparison.Ordinal))
        {
            yield return @"C:\Windows\Fonts\YuMincho.ttc";
            yield return @"C:\Windows\Fonts\yumin.ttf";
        }

        if (familyName.Contains("Meiryo", StringComparison.OrdinalIgnoreCase)
            || familyName.Contains("メイリオ", StringComparison.Ordinal))
        {
            yield return @"C:\Windows\Fonts\meiryo.ttc";
            yield return @"C:\Windows\Fonts\meiryob.ttc";
        }

        if (familyName.Contains("Arial", StringComparison.OrdinalIgnoreCase))
        {
            yield return "/System/Library/Fonts/Supplemental/Arial.ttf";
            yield return @"C:\Windows\Fonts\arial.ttf";
        }
    }

    private static string? FindFirstExistingPath(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static IEnumerable<string> GothicFontCandidates()
    {
        yield return "/Library/Fonts/Arial Unicode.ttf";
        yield return "/System/Library/Fonts/Supplemental/Arial Unicode.ttf";
        yield return "/System/Library/Fonts/ヒラギノ角ゴシック W8.ttc";
        yield return "/System/Library/Fonts/ヒラギノ角ゴシック W9.ttc";
        yield return "/System/Library/Fonts/Supplemental/Arial.ttf";
        yield return @"C:\Windows\Fonts\meiryo.ttc";
        yield return @"C:\Windows\Fonts\YuGothR.ttc";
        yield return @"C:\Windows\Fonts\YuGothM.ttc";
        yield return @"C:\Windows\Fonts\arial.ttf";
        yield return @"/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
    }

    private static IEnumerable<string> GothicBoldFontCandidates()
    {
        yield return "/Library/Fonts/Arial Unicode.ttf";
        yield return "/System/Library/Fonts/Supplemental/Arial Unicode.ttf";
        yield return "/System/Library/Fonts/ヒラギノ角ゴシック W8.ttc";
        yield return "/System/Library/Fonts/ヒラギノ角ゴシック W9.ttc";
        yield return "/System/Library/Fonts/Supplemental/Arial Bold.ttf";
        yield return "/System/Library/Fonts/Supplemental/Arial.ttf";
        yield return @"C:\Windows\Fonts\meiryob.ttc";
        yield return @"C:\Windows\Fonts\YuGothB.ttc";
        yield return @"C:\Windows\Fonts\YuGothM.ttc";
        yield return @"C:\Windows\Fonts\arialbd.ttf";
        yield return @"C:\Windows\Fonts\arial.ttf";
        yield return @"/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf";
    }

    private static IEnumerable<string> MinchoFontCandidates()
    {
        yield return "/System/Library/Fonts/ヒラギノ明朝 ProN.ttc";
        yield return "/System/Library/Fonts/ヒラギノ明朝 Pro.ttc";
        yield return "/System/Library/Fonts/Supplemental/Times New Roman.ttf";
        yield return @"C:\Windows\Fonts\YuMincho.ttc";
        yield return @"C:\Windows\Fonts\yumin.ttf";
        yield return @"C:\Windows\Fonts\msmincho.ttc";
        yield return @"/usr/share/fonts/opentype/noto/NotoSerifCJK-Regular.ttc";
        yield return @"/usr/share/fonts/truetype/dejavu/DejaVuSerif.ttf";
    }

    private static IEnumerable<string> RoundedFontCandidates()
    {
        yield return "/System/Library/Fonts/ヒラギノ丸ゴ ProN W4.ttc";
        yield return "/System/Library/Fonts/Supplemental/Arial Rounded Bold.ttf";
        yield return @"C:\Windows\Fonts\YuGothR.ttc";
        yield return @"C:\Windows\Fonts\meiryo.ttc";
        yield return @"/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
    }

    private static IEnumerable<string> LatinFontCandidates()
    {
        yield return "/System/Library/Fonts/Supplemental/Arial.ttf";
        yield return "/System/Library/Fonts/Supplemental/Helvetica.ttf";
        yield return @"C:\Windows\Fonts\arial.ttf";
        yield return @"/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
    }
}
