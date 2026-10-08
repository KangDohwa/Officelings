using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Officelings.Core;

public static class SpriteAtlas
{
    // Rows 0..6 and columns 0..12 retain their v1 meanings.
    public const int CellWidth = 16, CellHeight = 24, Columns = 45, Rows = 49, Variants = 8, BubbleRow = 6;
    public static AtlasInfo Info => new("atlas.png", CellWidth * Columns, CellHeight * Rows, CellWidth, CellHeight, Columns, Rows, 2);

    public static uint Hash(string id)
    {
        uint hash = 2166136261;
        foreach (var c in id) hash = unchecked((hash ^ c) * 16777619);
        return hash;
    }
    public static int Variant(string id) => (int)(Hash(id) % Variants);
    public static int Row(Provider provider, bool subagent, string id, int? identity = null)
    {
        var variant = identity ?? Variant(id); var role = (int)provider * 2 + (subagent ? 1 : 0);
        return variant == 0 ? role : 7 + (variant - 1) * 6 + role;
    }
    public static int Frame(string action, string facing, long milliseconds, int variant = 0)
    {
        var tick = milliseconds / 160 + variant;
        return action switch
        {
            "entering" or "walking" or "exiting" => 13 + (facing switch { "up" => 1, "right" => 2, "left" => 3, _ => 0 }) * 4 + (int)(tick % 4),
            "typing" => 3 + (int)(tick % 2),
            "thinking" => 5, "waiting" => 6,
            "pickup" => 29 + (int)(tick % 2),
            "reporting" => 31 + (int)(tick % 2),
            "coffee" => 33 + (int)(tick / 4 % 2),
            "tv" => 35 + (int)(tick / 5 % 2),
            "phone" => 37 + (int)(tick / 3 % 2),
            "napping" => 39 + (int)(tick / 6 % 2),
            "chatting" => 41 + (int)(tick / 3 % 2),
            "water" => 43 + (int)(tick / 4 % 2),
            _ => 0
        };
    }

    public static byte[] CreatePng()
    {
        var info = Info;
        var pixels = new byte[info.Width * info.Height * 4];
        void Rect(int x, int y, int w, int h, uint rgba)
        {
            for (var yy = y; yy < y + h; yy++) for (var xx = x; xx < x + w; xx++)
            {
                var p = (yy * info.Width + xx) * 4;
                pixels[p] = (byte)(rgba >> 24); pixels[p + 1] = (byte)(rgba >> 16);
                pixels[p + 2] = (byte)(rgba >> 8); pixels[p + 3] = (byte)rgba;
            }
        }
        uint[][] shirts =
        [
            [0x329b91ff,0x4bb3a2ff,0x298e85ff,0x62bfa8ff,0x3da99dff,0x248d94ff,0x68b9acff,0x258879ff],
            [0xd57e4eff,0xe69b62ff,0xc87047ff,0xe8ab75ff,0xd68759ff,0xca764eff,0xeb965bff,0xba6647ff],
            [0x7487d8ff,0x987bd8ff,0x658fccff,0xa289dcff,0x637ac9ff,0x8d72c9ff,0x749dd9ff,0xa077cdff]
        ];
        uint[] skins = [0xf1c8a0ff,0xdca77aff,0xb97f5eff,0xf6d6b3ff,0x9a674fff,0xe5b48dff,0xcc9771ff,0xf0c4a2ff];
        uint[] hairs = [0x45343aff,0x7e4935ff,0x292f3cff,0xd3aa61ff,0x373b4bff,0xa56b43ff,0x57463eff,0xc69a66ff];
        const uint ink = 0x303440ff, white = 0xfff4dfff, trouser = 0x485369ff;
        for (var row = 0; row < Rows; row++)
        {
            if (row == BubbleRow) continue;
            var variant = row < 6 ? 0 : 1 + (row - 7) / 6;
            var role = row < 6 ? row : (row - 7) % 6;
            var skin = skins[variant]; var hair = hairs[variant]; var shirt = shirts[role / 2][variant];
            for (var frame = 0; frame < Columns; frame++)
            {
                void R(int x, int y, int w, int h, uint color)
                {
                    var left = Math.Clamp(x, 0, 16); var top = Math.Clamp(y, 0, 24);
                    var right = Math.Clamp(x + w, 0, 16); var bottom = Math.Clamp(y + h, 0, 24);
                    if (right > left && bottom > top) Rect(frame * 16 + left, row * 24 + top, right - left, bottom - top, color);
                }
                var direction = frame is >= 13 and <= 28 ? (frame - 13) / 4 : frame is 1 or 2 ? 2 : frame is 3 or 4 or 9 or 29 or 30 or 35 or 36 ? 1 : 0;
                var step = frame is >= 13 and <= 28 ? (frame - 13) % 4 : frame is 1 or 2 ? frame : 0;
                var bob = step == 1 || step == 3 || frame == 36 ? 1 : 0;
                R(3, 23, 10, 1, 0x30344040);
                if (frame is 11 or 39 or 40)
                {
                    R(1, 12, 6, 6, ink); R(2, 12, 4, 2, hair); R(2, 14, 5, 3, skin);
                    R(3, 15, 2, 1, ink); R(7, 13, 6, 5, ink); R(7, 14, 5, 3, shirt);
                    R(12, 14, 3, 4, trouser); R(14, 17, 2, 2, ink);
                    R(6, frame == 40 ? 17 : 16, 4, 1, skin);
                    continue;
                }
                // Full cell silhouette: a large readable face, shoulders, hands and shoes.
                R(4, 1 + bob, 8, 1, ink); R(3, 2 + bob, 10, 7, ink);
                R(4, 3 + bob, 8, 6, skin); R(3, 5 + bob, 1, 3, skin); R(12, 5 + bob, 1, 3, skin);
                R(4, 2 + bob, 8, 2, hair); R(4, 4 + bob, 2 + variant % 3, 1, hair);
                if (variant is 2 or 4) { R(2, 3 + bob, 2, 7, hair); R(12, 3 + bob, 2, 7, hair); }
                if (variant == 3) R(5, bob, 5, 2, hair);
                if (direction == 1) R(4, 3 + bob, 8, 5, hair);
                else if (direction is 2 or 3)
                {
                    R(direction == 2 ? 5 : 9, 4 + bob, 3, 4, hair);
                    R(direction == 2 ? 10 : 5, 6 + bob, 1, 2, ink);
                    R(direction == 2 ? 12 : 3, 7 + bob, 1, 1, skin);
                }
                else
                {
                    R(5, 6 + bob, 1, 2, ink); R(10, 6 + bob, 1, 2, ink);
                    R(7, 8 + bob, 2, 1, 0xc17f66ff);
                    if (variant is 1 or 6) { R(4, 5 + bob, 3, 3, ink); R(9, 5 + bob, 3, 3, ink); R(5, 6 + bob, 1, 1, skin); R(10, 6 + bob, 1, 1, skin); R(7, 6 + bob, 2, 1, ink); }
                }
                R(6, 9 + bob, 4, 2, skin);
                R(3, 11 + bob, 10, 7, ink); R(4, 11 + bob, 8, 6, shirt);
                R(4, 17 + bob, 8, 1, ink);
                var arm = step == 1 ? 1 : step == 3 ? -1 : 0;
                R(2, 11 + bob + arm, 2, 4, shirt); R(2, 15 + bob + arm, 2, 3, skin);
                R(12, 11 + bob - arm, 2, 4, shirt); R(12, 15 + bob - arm, 2, 3, skin);
                R(4, 18, 3, step == 1 ? 3 : 4, trouser); R(9, 18, 3, step == 3 ? 3 : 4, trouser);
                R(3, step == 1 ? 21 : 22, 4, 1, ink); R(9, step == 3 ? 21 : 22, 4, 1, ink);
                if (role % 2 == 0)
                {
                    if (direction != 1) { R(6, 11 + bob, 4, 1, white); R(7, 12 + bob, 2, 4, ink); R(10, 13 + bob, 2, 2, 0xf4d57fff); }
                    else R(6, 12 + bob, 4, 1, 0xcfe8dfff);
                }
                else { R(5, 11 + bob, 1, 4, white); R(6, 15 + bob, 2, 2, white); R(6, 15 + bob, 1, 1, shirt); }
                if (frame is 3 or 4 or 29 or 30)
                {
                    R(2, 11, 3, 2, skin); R(11, frame % 2 == 0 ? 10 : 11, 3, 2, skin);
                    if (frame is 29 or 30) { R(10, 9, 5, 6, white); R(11, 11, 3, 1, shirt); }
                }
                if (frame is 7 or 31 or 32) { R(frame == 32 ? 0 : 1, 11, 5, 7, white); R(2, 13, 3, 1, shirt); R(4, 14, 2, 2, skin); }
                if (frame is 8 or 33 or 34 or 43 or 44)
                {
                    var cupY = frame is 34 or 44 ? 7 : 13;
                    R(11, cupY, 4, 4, frame >= 43 ? 0xb7dfedff : white); R(14, cupY + 1, 2, 2, ink); R(11, cupY + 3, 2, 2, skin);
                    if (frame < 43) R(12, cupY - 2, 1, 1, 0xbca789ff);
                }
                if (frame is 10 or 37 or 38)
                { R(11, 12, 4, 6, ink); R(12, 13, 2, 4, 0xaed9e2ff); R(12, frame == 38 ? 14 : 16, 2, 1, white); R(10, 16, 2, 2, skin); }
                if (frame is 12 or 41 or 42) { R(12, frame == 42 ? 9 : 12, 2, 3, skin); R(13, frame == 42 ? 8 : 11, 2, 1, skin); }
            }
        }
        for (var i = 0; i < 9; i++)
        {
            void R(int x, int y, int w, int h, uint color) => Rect(i * 16 + x, BubbleRow * 24 + y, w, h, color);
            R(1, 1, 14, 12, ink); R(2, 2, 12, 10, white); R(6, 13, 3, 2, ink); R(7, 12, 1, 2, white);
            switch (i)
            {
                case 0: R(4,4,2,1,ink); R(6,5,1,1,ink); R(4,6,2,1,ink); R(8,8,3,1,ink); break;
                case 1: R(5,3,6,7,ink); R(6,4,4,5,white); R(7,6,2,1,ink); break;
                case 2: R(4,3,5,5,ink); R(5,4,3,3,white); R(9,8,3,2,ink); break;
                case 3: R(4,3,3,3,ink); R(9,7,3,3,ink); R(6,6,4,1,ink); break;
                case 4: R(7,3,2,6,ink); R(5,5,6,2,ink); break;
                case 5: R(7,3,2,4,0xd88745ff); R(7,9,2,1,0xd88745ff); break;
                case 6:
                    for (var z = 0; z < 3; z++) { var x = 3 + z * 4; var y = 6 - z; R(x,y,3,1,ink); R(x+1,y+1,1,1,ink); R(x,y+2,3,1,ink); }
                    break;
                case 7: R(4,6,2,2,ink); R(7,6,2,2,ink); R(10,6,2,2,ink); break;
                case 8: R(5,4,1,2,0xbca789ff); R(8,3,1,3,0xbca789ff); R(11,4,1,2,0xbca789ff); break;
            }
        }
        using var output = new MemoryStream(); output.Write([137,80,78,71,13,10,26,10]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, info.Width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), info.Height);
        header[8] = 8; header[9] = 6;
        Chunk(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, true))
            for (var y = 0; y < info.Height; y++) { z.WriteByte(0); z.Write(pixels, y * info.Width * 4, info.Width * 4); }
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
