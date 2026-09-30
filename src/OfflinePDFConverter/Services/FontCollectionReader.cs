using System.Buffers.Binary;

namespace OfflinePDFConverter.Services;

/// <summary>Provides a standalone sfnt face from system TTC fonts for PDFsharp.</summary>
public static class FontCollectionReader
{
    public static byte[] ExtractFirstFace(byte[] data)
    {
        if (data.Length < 4 || !data.AsSpan(0, 4).SequenceEqual("ttcf"u8)) return data;
        void Require(long offset, long size)
        {
            if (offset < 0 || size < 0 || offset > data.Length - size)
                throw new InvalidDataException("フォントコレクションのデータが不正です。");
        }
        Require(0, 16);
        var faces = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8, 4));
        if (faces == 0) throw new InvalidDataException("フォントがありません。");
        Require(12, (long)faces * 4);
        var face = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12, 4));
        Require(face, 12);
        var count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan((int)face + 4, 2));
        var directorySize = 12 + count * 16;
        Require(face, directorySize);
        var tables = new List<(int Directory, int Source, int Length, int Target)>();
        var length = directorySize;
        for (var i = 0; i < count; i++)
        {
            var entry = (int)face + 12 + i * 16;
            var source = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entry + 8, 4));
            var size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entry + 12, 4));
            Require(source, size);
            tables.Add((12 + i * 16, (int)source, (int)size, length));
            length = checked(length + checked(((int)size + 3) & ~3));
        }
        var result = new byte[length];
        data.AsSpan((int)face, directorySize).CopyTo(result);
        int? head = null;
        foreach (var table in tables)
        {
            data.AsSpan(table.Source, table.Length).CopyTo(result.AsSpan(table.Target));
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(table.Directory + 8, 4), (uint)table.Target);
            if (result.AsSpan(table.Directory, 4).SequenceEqual("head"u8) && table.Length >= 12)
                head = table.Target;
        }
        if (head is { } h)
        {
            // The standalone face needs its own whole-font checksum adjustment.
            result.AsSpan(h + 8, 4).Clear();
            uint sum = 0;
            for (var offset = 0; offset < result.Length; offset += 4)
                sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(result.AsSpan(offset, 4)));
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(h + 8, 4), unchecked(0xB1B0AFBAu - sum));
        }
        return result;
    }
}
