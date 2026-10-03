using System.Buffers.Binary;
using System.IO.Compression;

namespace Vibedotcom.Core.Iso;

// The source rigs publish no standing shadow. This small ground primitive has the frozen 16x6 contact footprint.
internal static class IsoActorShadow
{
    public const string SheetId = "iso/core-contact-shadow";
    public static byte[] Png()
    {
        const int width = 32, height = 32;
        var data = new byte[height * (1 + width * 4)];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var contact = Math.Pow((x + .5 - 16) / 8, 2) + Math.Pow((y + .5 - 20) / 3, 2) <= 1;
            var cast = Math.Pow((x + .5 - 20) / 10, 2) + Math.Pow((y + .5 - 22) / 3, 2) <= 1;
            if (!contact && !cast) continue;
            var index = y * (width * 4 + 1) + 1 + x * 4;
            data[index] = 0x11; data[index + 1] = 0x19; data[index + 2] = 0x24; data[index + 3] = contact ? (byte)96 : (byte)48;
        }
        using var png = new MemoryStream(); png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 6; Chunk(png, "IHDR"u8, header);
        using var buffer = new MemoryStream();
        using (var z = new ZLibStream(buffer, CompressionLevel.SmallestSize, true)) z.Write(data);
        Chunk(png, "IDAT"u8, buffer.ToArray()); Chunk(png, "IEND"u8, []); return png.ToArray();
    }
    private static void Chunk(Stream output, ReadOnlySpan<byte> name, ReadOnlySpan<byte> data)
    {
        Span<byte> value = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(value, data.Length); output.Write(value); output.Write(name); output.Write(data);
        uint crc = 0xffffffff;
        foreach (var b in name) crc = Add(crc, b);
        foreach (var b in data) crc = Add(crc, b);
        BinaryPrimitives.WriteUInt32BigEndian(value, crc ^ 0xffffffff); output.Write(value);
    }
    private static uint Add(uint crc, byte value)
    { crc ^= value; for (var k = 0; k < 8; k++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); return crc; }
}
