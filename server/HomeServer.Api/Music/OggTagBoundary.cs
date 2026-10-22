using System.Buffers.Binary;
using HomeServer.Core;

namespace HomeServer.Music;

// TagLibSharp 2.3 can omit the zero lacing terminator when a header packet is
// exactly N*255 bytes. Repair only that provable header boundary in a new copy.
public static class OggTagBoundary
{
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(value =>
    {
        var crc = (uint)value << 24; for (var bit = 0; bit < 8; bit++) crc = crc << 1 ^ ((crc & 0x80000000) == 0 ? 0u : 0x04c11db7u); return crc;
    }).ToArray();
    public static async Task<int> RepairAsync(string path, CancellationToken token)
    {
        var temporary = path + ".ogg-boundary"; var fixes = 0;
        if (File.Exists(temporary)) { AppFiles.RejectLink(temporary); File.Delete(temporary); }
        try
        {
            await using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                var current = await ReadPage(input, token); var headers = new HashSet<uint>(); var shifts = new Dictionary<uint, uint>();
                while (current is not null)
                {
                    token.ThrowIfCancellationRequested(); var next = await ReadPage(input, token); var count = current[26]; var body = current.AsSpan(27 + count);
                    var serial = BinaryPrimitives.ReadUInt32LittleEndian(current.AsSpan(14)); var sequence = BinaryPrimitives.ReadUInt32LittleEndian(current.AsSpan(18));
                    var granule = BinaryPrimitives.ReadInt64LittleEndian(current.AsSpan(6));
                    if (body.StartsWith("OpusTags"u8) || body.StartsWith("\x03vorbis"u8)) headers.Add(serial);
                    var unterminated = count > 0 && current[26 + count] == 255;
                    var repair = headers.Contains(serial) && unterminated && granule is 0 or -1 && (current[5] & 4) == 0 && next is not null
                        && BinaryPrimitives.ReadUInt32LittleEndian(next.AsSpan(14)) == serial && (next[5] & 1) == 0;
                    var shift = shifts.GetValueOrDefault(serial); byte[]? terminator = null;
                    if (repair)
                    {
                        if (count < 255)
                        {
                            var corrected = new byte[current.Length + 1]; current.AsSpan(0, 27 + count).CopyTo(corrected); current.AsSpan(27 + count).CopyTo(corrected.AsSpan(28 + count));
                            corrected[26]++; BinaryPrimitives.WriteInt64LittleEndian(corrected.AsSpan(6), 0); current = corrected;
                        }
                        else
                        {
                            terminator = new byte[28]; current.AsSpan(0, 27).CopyTo(terminator); terminator[5] = 1; terminator[26] = 1;
                            BinaryPrimitives.WriteInt64LittleEndian(terminator.AsSpan(6), 0);
                            BinaryPrimitives.WriteUInt32LittleEndian(terminator.AsSpan(18), checked(sequence + shift + 1));
                            shifts[serial] = checked(shift + 1);
                            BinaryPrimitives.WriteInt64LittleEndian(current.AsSpan(6), -1);
                        }
                        fixes++; headers.Remove(serial);
                    }
                    else if (!unterminated) headers.Remove(serial);
                    if (shift > 0) BinaryPrimitives.WriteUInt32LittleEndian(current.AsSpan(18), checked(sequence + shift));
                    WriteCrc(current); await output.WriteAsync(current, token);
                    if (terminator is not null) { WriteCrc(terminator); await output.WriteAsync(terminator, token); }
                    current = next;
                }
                await output.FlushAsync(token); output.Flush(true);
            }
            token.ThrowIfCancellationRequested(); if (fixes > 0) File.Move(temporary, path, true); return fixes;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static async Task<byte[]?> ReadPage(Stream input, CancellationToken token)
    {
        var header = new byte[27]; if (await input.ReadAsync(header.AsMemory(0, 1), token) == 0) return null;
        await input.ReadExactlyAsync(header.AsMemory(1), token);
        if (!header.AsSpan(0, 4).SequenceEqual("OggS"u8) || header[4] != 0) throw new DomainException("Ogg etiket kopyasının sayfa başlığı geçersiz.", 422);
        var laces = new byte[header[26]]; await input.ReadExactlyAsync(laces, token); var page = new byte[27 + laces.Length + laces.Sum(value => value)];
        header.CopyTo(page, 0); laces.CopyTo(page, 27); await input.ReadExactlyAsync(page.AsMemory(27 + laces.Length), token);
        var expected = BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(22)); page.AsSpan(22, 4).Clear(); var actual = Crc(page);
        if (expected != actual) throw new DomainException("Ogg etiket kopyasının sayfa CRC'si doğrulanamadı.", 422);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(22), expected); return page;
    }
    private static uint Crc(byte[] page)
    { uint crc = 0; foreach (var value in page) crc = crc << 8 ^ CrcTable[(crc >> 24) ^ value]; return crc; }
    private static void WriteCrc(byte[] page)
    { page.AsSpan(22, 4).Clear(); BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(22), Crc(page)); }
}
