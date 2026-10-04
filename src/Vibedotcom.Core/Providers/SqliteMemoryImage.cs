using System.Buffers.Binary;

namespace Vibedotcom.Core.Providers;

// Private mode must not spill provider text into disposable DB copies. Recover the
// last committed WAL pages in memory, then let SQLite validate/query the image.
// Format: https://www.sqlite.org/fileformat2.html#wal_file_format
internal static class SqliteMemoryImage
{
    private const int Limit = 256 * 1024 * 1024;
    internal static byte[] Read(string source)
    {
        Span<byte> journalHeader = stackalloc byte[8];
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var stamp = SqliteSnapshot.Stamp(source);
            var image = Bytes(source);
            var wal = File.Exists(source + "-wal") ? Bytes(source + "-wal") : [];
            try
            {
                if (File.Exists(source + "-journal"))
                {
                    using var journal = Open(source + "-journal");
                    if (journal.Read(journalHeader) == 8 && journalHeader.SequenceEqual(new byte[] { 0xd9,0xd5,0x05,0xf9,0x20,0xa1,0x63,0xd7 }))
                        throw new IOException("Antigravity rollback journal is active; using cached state.");
                }
                if (stamp != SqliteSnapshot.Stamp(source)) { Array.Clear(image); continue; }
                var merged = Merge(image,wal);
                if (!ReferenceEquals(merged,image)) Array.Clear(image);
                return merged;
            }
            catch { Array.Clear(image); throw; }
            finally { Array.Clear(wal); }
        }
        throw new IOException("Antigravity snapshot remained unstable.");
    }

    private static FileStream Open(string path) => new(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite | FileShare.Delete);
    private static byte[] Bytes(string path)
    {
        using var stream = Open(path);
        if (stream.Length > Limit) throw new IOException("Antigravity memory snapshot exceeds 256 MiB.");
        var result = new byte[(int)stream.Length]; stream.ReadExactly(result); return result;
    }
    private static uint Big(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt32BigEndian(bytes);
    private static void Checksum(ReadOnlySpan<byte> bytes, bool bigEndian, ref uint first, ref uint second)
    {
        unchecked
        {
            for (var offset = 0; offset < bytes.Length; offset += 8)
            {
                first += (bigEndian ? Big(bytes[offset..]) : BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..])) + second;
                second += (bigEndian ? Big(bytes[(offset+4)..]) : BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset+4)..])) + first;
            }
        }
    }
    internal static byte[] Merge(byte[] database, byte[] wal)
    {
        if (database.Length < 100 || !database.AsSpan(0,16).SequenceEqual("SQLite format 3\0"u8))
            throw new IOException("Antigravity database header is invalid.");
        var pageSize = (int)BinaryPrimitives.ReadUInt16BigEndian(database.AsSpan(16));
        if (pageSize == 1) pageSize = 65536;
        if (pageSize is < 512 or > 65536 || (pageSize & (pageSize - 1)) != 0 || database.Length % pageSize != 0)
            throw new IOException("Antigravity database page size is invalid.");
        var image = database;
        if (wal.Length > 0)
        {
            if (wal.Length < 32 || Big(wal) is not (0x377f0682 or 0x377f0683) || Big(wal.AsSpan(4)) != 3007000 || Big(wal.AsSpan(8)) != pageSize)
                throw new IOException("Antigravity WAL header is invalid.");
            var bigEndian = Big(wal) == 0x377f0683;
            uint first=0, second=0; Checksum(wal.AsSpan(0,24),bigEndian,ref first,ref second);
            if (first != Big(wal.AsSpan(24)) || second != Big(wal.AsSpan(28))) throw new IOException("Antigravity WAL checksum is invalid.");
            var committedEnd = 32; uint pages=0;
            for (var offset=32; offset <= wal.Length-pageSize-24; offset+=pageSize+24)
            {
                var frame=wal.AsSpan(offset,24);
                if (!frame[8..16].SequenceEqual(wal.AsSpan(16,8)) || Big(frame) == 0) break;
                Checksum(frame[..8],bigEndian,ref first,ref second);
                Checksum(wal.AsSpan(offset+24,pageSize),bigEndian,ref first,ref second);
                if (first != Big(frame[16..]) || second != Big(frame[20..])) break;
                if (Big(frame[4..]) != 0) { pages=Big(frame[4..]); committedEnd=offset+pageSize+24; }
            }
            if (pages != 0)
            {
                if ((long)pages * pageSize > Limit) throw new IOException("Antigravity memory snapshot exceeds 256 MiB.");
                image=new byte[(int)pages*pageSize]; database.AsSpan(0,Math.Min(image.Length,database.Length)).CopyTo(image);
                for (var offset=32; offset<committedEnd; offset+=pageSize+24)
                {
                    var page=Big(wal.AsSpan(offset));
                    if (page <= pages) wal.AsSpan(offset+24,pageSize).CopyTo(image.AsSpan(((int)page-1)*pageSize,pageSize));
                }
                BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(28),pages);
            }
        }
        // sqlite3_deserialize cannot open a WAL-mode image; all committed pages are now merged.
        image[18]=image[19]=1;
        return image;
    }
}
