using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Vibedotcom.Core;

public static class SpriteAtlas
{
    public const int CellWidth = 16, CellHeight = 24, Columns = 13, Rows = 7;
    public static AtlasInfo Info => new("atlas.png", CellWidth * Columns, CellHeight * Rows, CellWidth, CellHeight, Columns, Rows, 1);

    public static byte[] CreatePng()
    {
        var pixels = new byte[Info.Width * Info.Height * 4];
        void Rect(int x, int y, int w, int h, uint rgba)
        {
            for (var yy = y; yy < y + h; yy++) for (var xx = x; xx < x + w; xx++)
            {
                var p = (yy * Info.Width + xx) * 4;
                pixels[p] = (byte)(rgba >> 24); pixels[p + 1] = (byte)(rgba >> 16);
                pixels[p + 2] = (byte)(rgba >> 8); pixels[p + 3] = (byte)rgba;
            }
        }
        uint[] shirts = [0x409e88ff, 0xcd7755ff, 0x7c89c9ff];
        const uint ink = 0x303942ff, skin = 0xefc097ff, white = 0xfff6dfff;
        for (var row = 0; row < 6; row++) for (var frame = 0; frame < Columns; frame++)
        {
            var x = frame * CellWidth; var y = row * CellHeight;
            var step = frame is 1 or 4 ? 1 : 0;
            Rect(x + 3, y + 22, 10, 2, 0x30394255); // shadow
            Rect(x + 5, y + 6, 6, 7, ink);
            Rect(x + 4, y + 8, 8, 3, skin);
            Rect(x + 5, y + 7, 6, 2, row / 2 == 2 ? 0x879dd2ff : ink);
            Rect(x + 6, y + 10, 1, 1, ink); Rect(x + 9, y + 10, 1, 1, ink);
            Rect(x + 4, y + 13, 8, 7, ink);
            Rect(x + 5, y + 13, 6, 6, shirts[row / 2]);
            Rect(x + 3, y + 14 + step, 2, 4, skin); Rect(x + 11, y + 14 - step, 2, 4, skin);
            Rect(x + 5, y + 20, 2, 3 - step, ink); Rect(x + 9, y + 20 + step, 2, 3 - step, ink);
            if (row % 2 == 0) { Rect(x + 7, y + 13, 2, 1, white); Rect(x + 7, y + 14, 1, 4, ink); }
            switch (frame)
            {
                case 3: case 4: Rect(x + 2, y + 18, 12, 2, white); Rect(x + 4 + step, y + 17, 3, 1, skin); break;
                case 5: Rect(x + 7, y + 1, 2, 2, white); Rect(x + 10, y + 3, 1, 1, white); break;
                case 6: Rect(x + 7, y + 0, 2, 3, 0xe5ba66ff); Rect(x + 7, y + 4, 2, 1, 0xe5ba66ff); break;
                case 7: Rect(x + 10, y + 12, 5, 7, white); Rect(x + 11, y + 14, 3, 1, shirts[row / 2]); break;
                case 8: Rect(x + 11, y + 13, 4, 4, white); Rect(x + 12, y + 11, 1, 1, 0xc7ba9dff); break;
                case 9: Rect(x + 7, y + 2, 4, 1, white); break;
                case 10: Rect(x + 11, y + 12, 3, 5, ink); Rect(x + 12, y + 13, 1, 3, 0x97cbd3ff); break;
                case 11: Rect(x + 8, y + 2, 4, 1, white); Rect(x + 10, y + 3, 1, 1, white); Rect(x + 8, y + 4, 4, 1, white); break;
                case 12: Rect(x + 6, y + 0, 7, 4, white); Rect(x + 7, y + 4, 1, 1, white); break;
            }
        }
        for (var i = 0; i < 5; i++)
        {
            var x = i * CellWidth; var y = 6 * CellHeight;
            Rect(x + 1, y + 1, 14, 12, white); Rect(x + 7, y + 13, 2, 2, white);
            switch (i)
            {
                case 0: Rect(x + 4, y + 4, 2, 1, ink); Rect(x + 6, y + 5, 1, 1, ink); Rect(x + 4, y + 6, 2, 1, ink); Rect(x + 8, y + 8, 3, 1, ink); break;
                case 1: Rect(x + 5, y + 3, 6, 7, ink); Rect(x + 6, y + 4, 4, 5, white); break;
                case 2: Rect(x + 4, y + 3, 5, 5, ink); Rect(x + 5, y + 4, 3, 3, white); Rect(x + 9, y + 8, 3, 2, ink); break;
                case 3: Rect(x + 4, y + 3, 3, 3, ink); Rect(x + 9, y + 7, 3, 3, ink); Rect(x + 6, y + 6, 4, 1, ink); break;
                default: Rect(x + 7, y + 3, 2, 6, ink); Rect(x + 5, y + 5, 6, 2, ink); break;
            }
        }
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, Info.Width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), Info.Height);
        header[8] = 8; header[9] = 6; // 8-bit RGBA, no interlace
        Chunk(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, true))
            for (var y = 0; y < Info.Height; y++) { z.WriteByte(0); z.Write(pixels, y * Info.Width * 4, Info.Width * 4); }
        Chunk(output, "IDAT", compressed.ToArray()); Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, data.Length); output.Write(length);
        var name = Encoding.ASCII.GetBytes(type); output.Write(name); output.Write(data);
        uint crc = 0xffffffff;
        foreach (var b in name.Concat(data))
        { crc ^= b; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
        BinaryPrimitives.WriteUInt32BigEndian(length, crc ^ 0xffffffff); output.Write(length);
    }
}
