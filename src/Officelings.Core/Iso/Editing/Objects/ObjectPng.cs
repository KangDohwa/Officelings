using System.Buffers.Binary;
using System.IO.Compression;

namespace Officelings.Core.Iso.Editing;

internal sealed record ObjectPng(int Width, int Height, byte[] Rgba)
{
    internal byte Alpha(int x, int y) => Rgba[(y * Width + x) * 4 + 3];
    internal static (int Width, int Height) Size(byte[] bytes)
    {
        if (bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8)) != 13 || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("PNG header.");
        var w = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16));
        var h = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20));
        if (w is 0 or > 2048 || h is 0 or > 2048 || (ulong)w * h > 4_194_304) throw new ObjectSizeException();
        return ((int)w, (int)h);
    }
    internal static ObjectPng Decode(byte[] bytes, CancellationToken cancellation = default)
    {
        var (width, height) = Size(bytes);
        var color = bytes[25];
        if (bytes[24] != 8 || color is not (2 or 3 or 6) || bytes[26] != 0 || bytes[27] != 0 || bytes[28] != 0)
            throw new InvalidDataException("PNG format.");
        byte[] palette = [], transparency = [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using var idat = new MemoryStream();
        var ended = false; var dataEnded = false;
        for (var pos = 8; pos < bytes.Length;)
        {
            cancellation.ThrowIfCancellationRequested();
            if (bytes.Length - pos < 12) throw new InvalidDataException("PNG chunks.");
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(pos));
            if (length > bytes.Length - pos - 12) throw new InvalidDataException("PNG chunk bounds.");
            var n = (int)length; var type = bytes.AsSpan(pos + 4, 4); var data = bytes.AsSpan(pos + 8, n);
            if (Crc(bytes.AsSpan(pos + 4, n + 4)) != BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(pos + 8 + n)))
                throw new InvalidDataException("PNG CRC.");
            var name = System.Text.Encoding.ASCII.GetString(type);
            if (name != "IDAT" && !seen.Add(name)) throw new InvalidDataException("Duplicate PNG chunk.");
            if (seen.Contains("IDAT") && name != "IDAT") dataEnded = true;
            switch (name)
            {
                case "IHDR": if (pos != 8 || n != 13) throw new InvalidDataException("PNG IHDR."); break;
                case "PLTE":
                    if (seen.Contains("IDAT") || n is < 3 or > 768 || n % 3 != 0 || seen.Contains("tRNS")) throw new InvalidDataException("PNG palette.");
                    palette = data.ToArray(); break;
                case "tRNS":
                    if (seen.Contains("IDAT") || color == 6 || color == 2 && n != 6 || color == 3 && (n == 0 || n > palette.Length / 3))
                        throw new InvalidDataException("PNG transparency.");
                    transparency = data.ToArray(); break;
                case "IDAT":
                    if (dataEnded || color == 3 && palette.Length == 0) throw new InvalidDataException("PNG data order.");
                    seen.Add(name); idat.Write(data); break;
                case "IEND":
                    if (n != 0 || pos + 12 != bytes.Length || idat.Length == 0) throw new InvalidDataException("PNG end.");
                    ended = true; break;
                // Only inert, bounded color/display metadata is accepted. Text, EXIF, APNG and unknown payloads fail closed.
                case "gAMA": if (n != 4 || seen.Contains("IDAT")) throw new InvalidDataException("PNG gamma."); break;
                case "cHRM": if (n != 32 || seen.Contains("IDAT")) throw new InvalidDataException("PNG chromaticity."); break;
                case "sRGB": if (n != 1 || data[0] > 3 || seen.Contains("IDAT")) throw new InvalidDataException("PNG color intent."); break;
                case "pHYs": if (n != 9 || data[8] > 1 || seen.Contains("IDAT")) throw new InvalidDataException("PNG physical size."); break;
                default: throw new InvalidDataException("Unsupported PNG chunk.");
            }
            pos += n + 12;
        }
        if (!ended) throw new InvalidDataException("Missing PNG end.");
        var channels = color == 2 ? 3 : color == 6 ? 4 : 1;
        var stride = width * channels; var expected = checked((stride + 1) * height);
        var raw = new byte[expected];
        var compressed = idat.ToArray();
        using var input = new ExactPngInput(compressed, cancellation);
        using (var z = new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true))
        { z.ReadExactly(raw); if (z.ReadByte() != -1) throw new InvalidDataException("PNG decompression budget."); }
        if (input.Position != input.Length || compressed.Length < 6 || Adler(raw) != BinaryPrimitives.ReadUInt32BigEndian(compressed.AsSpan(compressed.Length - 4)))
            throw new InvalidDataException("PNG zlib checksum.");
        var result = new byte[width * height * 4];
        var prior = new byte[stride]; var row = new byte[stride];
        for (var y = 0; y < height; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            var filter = raw[y * (stride + 1)]; raw.AsSpan(y * (stride + 1) + 1, stride).CopyTo(row);
            for (var x = 0; x < stride; x++)
            {
                var a = x >= channels ? row[x - channels] : 0; var b = prior[x]; var c = x >= channels ? prior[x - channels] : 0;
                var add = filter switch { 0 => 0, 1 => a, 2 => b, 3 => (a + b) / 2, 4 => Paeth(a, b, c), _ => throw new InvalidDataException("PNG filter.") };
                row[x] = unchecked((byte)(row[x] + add));
            }
            for (var x = 0; x < width; x++)
            {
                var src = x * channels; var dst = (y * width + x) * 4;
                if (color == 3)
                {
                    var index = row[src]; if (index * 3 + 2 >= palette.Length) throw new InvalidDataException("PNG palette index.");
                    palette.AsSpan(index * 3, 3).CopyTo(result.AsSpan(dst));
                    result[dst + 3] = index < transparency.Length ? transparency[index] : (byte)255;
                }
                else
                {
                    row.AsSpan(src, 3).CopyTo(result.AsSpan(dst)); result[dst + 3] = color == 6 ? row[src + 3] : (byte)255;
                    if (color == 2 && transparency.Length == 6 && row[src] == BinaryPrimitives.ReadUInt16BigEndian(transparency) &&
                        row[src + 1] == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(2)) &&
                        row[src + 2] == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(4))) result[dst + 3] = 0;
                }
            }
            (row, prior) = (prior, row);
        }
        return new(width, height, result);
    }
    // Prevent ZLibStream read-ahead from hiding a second member or trailing IDAT payload.
    private sealed class ExactPngInput(byte[] bytes, CancellationToken cancellation) : MemoryStream(bytes, writable: false)
    {
        public override int Read(byte[] buffer, int offset, int count)
        { cancellation.ThrowIfCancellationRequested(); return base.Read(buffer, offset, Math.Min(count, 1)); }
        public override int Read(Span<byte> buffer)
        { cancellation.ThrowIfCancellationRequested(); return base.Read(buffer[..Math.Min(buffer.Length, 1)]); }
    }
    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c; var x = Math.Abs(p - a); var y = Math.Abs(p - b); var z = Math.Abs(p - c);
        return x <= y && x <= z ? a : y <= z ? b : c;
    }
    internal static uint Crc(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var b in bytes) { crc ^= b; for (var k = 0; k < 8; k++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
        return ~crc;
    }
    private static uint Adler(byte[] bytes)
    {
        uint a = 1, b = 0;
        foreach (var value in bytes) { a = (a + value) % 65521; b = (b + a) % 65521; }
        return (b << 16) | a;
    }
    internal static byte[] Encode(int width, int height, byte[] rgba)
    {
        using var png = new MemoryStream(); png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string name, byte[] payload)
        {
            var buffer = new byte[payload.Length + 12]; BinaryPrimitives.WriteInt32BigEndian(buffer, payload.Length);
            System.Text.Encoding.ASCII.GetBytes(name).CopyTo(buffer, 4); payload.CopyTo(buffer, 8);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(buffer.Length - 4), Crc(buffer.AsSpan(4, payload.Length + 4))); png.Write(buffer);
        }
        var ihdr = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(ihdr, width); BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; ihdr[9] = 6; Chunk("IHDR", ihdr);
        // Stored deflate blocks make thumbnail bytes independent of the platform zlib version.
        var raw = new byte[(width * 4 + 1) * height];
        for (var y = 0; y < height; y++) rgba.AsSpan(y * width * 4, width * 4).CopyTo(raw.AsSpan(y * (width * 4 + 1) + 1));
        using var data = new MemoryStream(); data.Write(new byte[] { 0x78, 0x01 });
        for (var offset = 0; offset < raw.Length;)
        {
            var count = Math.Min(65535, raw.Length - offset); data.WriteByte(offset + count == raw.Length ? (byte)1 : (byte)0);
            var lengths = new byte[4]; BinaryPrimitives.WriteUInt16LittleEndian(lengths, (ushort)count);
            BinaryPrimitives.WriteUInt16LittleEndian(lengths.AsSpan(2), (ushort)(65535 - count)); data.Write(lengths);
            data.Write(raw, offset, count); offset += count;
        }
        var checksum = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(checksum, Adler(raw)); data.Write(checksum);
        Chunk("IDAT", data.ToArray()); Chunk("IEND", []); return png.ToArray();
    }
}
