using System.Buffers.Binary;
using System.IO.Compression;

namespace Vibedotcom.Core.Iso;

// Local PNG pixels for contacts and alpha picking. It accepts the same formats/limits as AssetPack.PngSize.
internal sealed class IsoPngMask(int width, int height, byte[] rgba)
{
    public byte Alpha(int x, int y) => x < 0 || x >= width || y < 0 || y >= height ? (byte)0 : rgba[(y * width + x) * 4 + 3];
    public bool EqualCells(int w, int h, int rowA, int colA, int colB) => EqualCells(w, h, rowA, colA, rowA, colB);
    public bool EqualCells(int w, int h, int rowA, int colA, int rowB, int colB)
    {
        if (rowA == rowB && colA == colB) return true;
        for (var y = 0; y < h; y++)
        {
            var a = rgba.AsSpan(((rowA * h + y) * width + colA * w) * 4, w * 4);
            var b = rgba.AsSpan(((rowB * h + y) * width + colB * w) * 4, w * 4);
            if (a.SequenceEqual(b)) continue;
            for (var x = 0; x < w * 4; x += 4)
                if (a[x + 3] != b[x + 3] || a[x + 3] != 0 && !a.Slice(x, 3).SequenceEqual(b.Slice(x, 3))) return false;
        }
        return true;
    }
    public byte[] CopyCell(int x, int y, int w, int h)
    {
        if (x < 0 || y < 0 || w <= 0 || h <= 0 || x + w > width || y + h > height) throw new InvalidDataException("UI cell bounds.");
        var cell = new byte[w * h * 4];
        for (var row = 0; row < h; row++) rgba.AsSpan(((y + row) * width + x) * 4, w * 4).CopyTo(cell.AsSpan(row * w * 4));
        return cell;
    }
    public static IsoPngMask Read(byte[] bytes)
    {
        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20)); var color = bytes[25];
        var channels = color switch { 0 or 3 => 1, 2 => 3, 4 => 2, 6 => 4, _ => throw new InvalidDataException("PNG format.") };
        byte[] palette = [], transparency = [];
        using var compressed = new MemoryStream();
        for (var pos = 8; pos + 12 <= bytes.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(pos)); var name = bytes.AsSpan(pos + 4, 4);
            if (name.SequenceEqual("IDAT"u8)) compressed.Write(bytes, pos + 8, length);
            else if (name.SequenceEqual("PLTE"u8)) palette = bytes.AsSpan(pos + 8, length).ToArray();
            else if (name.SequenceEqual("tRNS"u8)) transparency = bytes.AsSpan(pos + 8, length).ToArray();
            else if (name.SequenceEqual("IEND"u8)) break;
            pos += length + 12;
        }
        compressed.Position = 0; using var z = new ZLibStream(compressed, CompressionMode.Decompress);
        var row = new byte[width * channels]; var previous = new byte[row.Length]; var result = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var filter = z.ReadByte(); z.ReadExactly(row);
            for (var x = 0; x < row.Length; x++)
            {
                var a = x >= channels ? row[x - channels] : 0; var b = previous[x]; var c = x >= channels ? previous[x - channels] : 0;
                var add = filter switch { 0 => 0, 1 => a, 2 => b, 3 => (a + b) / 2, 4 => Paeth(a, b, c), _ => throw new InvalidDataException("PNG filter.") };
                row[x] = unchecked((byte)(row[x] + add));
            }
            for (var x = 0; x < width; x++)
            {
                var src = x * channels; var dst = (y * width + x) * 4; var alpha = (byte)255;
                if (color is 0 or 4)
                {
                    result[dst] = result[dst + 1] = result[dst + 2] = row[src];
                    if (color == 4) alpha = row[src + 1];
                    else if (transparency.Length == 2 && row[src] == BinaryPrimitives.ReadUInt16BigEndian(transparency)) alpha = 0;
                }
                else if (color == 3)
                {
                    var index = row[src];
                    if (index * 3 + 2 >= palette.Length) throw new InvalidDataException("PNG palette index.");
                    result[dst] = palette[index * 3]; result[dst + 1] = palette[index * 3 + 1]; result[dst + 2] = palette[index * 3 + 2];
                    if (index < transparency.Length) alpha = transparency[index];
                }
                else
                {
                    result[dst] = row[src]; result[dst + 1] = row[src + 1]; result[dst + 2] = row[src + 2];
                    if (color == 6) alpha = row[src + 3];
                    else if (transparency.Length == 6 && row[src] == BinaryPrimitives.ReadUInt16BigEndian(transparency) &&
                        row[src + 1] == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(2)) &&
                        row[src + 2] == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(4))) alpha = 0;
                }
                result[dst + 3] = alpha;
            }
            (row, previous) = (previous, row);
        }
        return new(width, height, result);
    }
    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c; var pa = Math.Abs(p - a); var pb = Math.Abs(p - b); var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
