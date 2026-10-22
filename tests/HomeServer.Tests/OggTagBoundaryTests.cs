using System.Buffers.Binary;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Music;

namespace HomeServer.Tests;

public sealed class OggTagBoundaryTests
{
    [Theory]
    [InlineData(255)]
    [InlineData(65025)]
    public async Task MissingZeroTerminatorKeepsPacketsGranulesAndValidCrcEvenWhenThePageIsFull(int length)
    {
        using var home = new TestWorkspace(); var path = Path.Combine(home.Source, "isolated.opus");
        var comment = new byte[length]; "OpusTags"u8.CopyTo(comment); comment[^1] = 7;
        byte[] audio = [0xf8, 0xff, 0xfe];
        var head = Page(0, 2, 0, [19], new byte[19]); var tags = Page(1, 0, 0, Enumerable.Repeat((byte)255, length / 255).ToArray(), comment); var end = Page(2, 4, 48000, [3], audio);
        File.WriteAllBytes(path, [..head, ..tags, ..end]); Assert.Equal(1, await OggTagBoundary.RepairAsync(path, CancellationToken.None));
        var bytes = File.ReadAllBytes(path); var offset = 0; var sequence = 0; var packets = new List<byte[]>(); var packet = new List<byte>();
        while (offset < bytes.Length)
        {
            Assert.True(bytes.AsSpan(offset, 4).SequenceEqual("OggS"u8)); var count = bytes[offset + 26]; var laces = bytes.Skip(offset + 27).Take(count).ToArray(); var size = 27 + count + laces.Sum(value => value);
            var page = bytes.AsSpan(offset, size).ToArray(); var crc = BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(22)); page.AsSpan(22, 4).Clear(); Assert.Equal(crc, Checksum(page));
            Assert.Equal(sequence++, BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(18))); var position = offset + 27 + count;
            foreach (var lace in laces) { packet.AddRange(bytes.AsSpan(position, lace).ToArray()); position += lace; if (lace < 255) { packets.Add(packet.ToArray()); packet.Clear(); } }
            if ((page[5] & 4) != 0) Assert.Equal(48000, BinaryPrimitives.ReadInt64LittleEndian(page.AsSpan(6))); offset += size;
        }
        Assert.Empty(packet); Assert.Equal(3, packets.Count); Assert.Equal(comment, packets[1]); Assert.Equal(audio, packets[2]);
        var hash = StateFolders.Hash(path); Assert.Equal(0, await OggTagBoundary.RepairAsync(path, CancellationToken.None)); Assert.Equal(hash, StateFolders.Hash(path));
        Assert.Single(Directory.GetFiles(home.Source));
    }
    [Fact]
    public async Task BadCrcAndCancellationCannotReplaceTheCandidate()
    {
        using var home = new TestWorkspace(); var path = Path.Combine(home.Source, "isolated.ogg"); var page = Page(0, 2, 0, [19], new byte[19]); page[22] ^= 1; File.WriteAllBytes(path, page); var hash = StateFolders.Hash(path);
        await Assert.ThrowsAsync<DomainException>(() => OggTagBoundary.RepairAsync(path, CancellationToken.None)); Assert.Equal(hash, StateFolders.Hash(path));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OggTagBoundary.RepairAsync(path, cancelled.Token));
        Assert.Equal(hash, StateFolders.Hash(path)); Assert.Single(Directory.GetFiles(home.Source));
    }
    private static byte[] Page(int sequence, byte flags, long granule, byte[] laces, byte[] data)
    {
        var page = new byte[27 + laces.Length + data.Length]; "OggS"u8.CopyTo(page); page[5] = flags; page[26] = (byte)laces.Length;
        BinaryPrimitives.WriteInt64LittleEndian(page.AsSpan(6), granule); BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(14), 123); BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(18), sequence);
        laces.CopyTo(page, 27); data.CopyTo(page, 27 + laces.Length); BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(22), Checksum(page)); return page;
    }
    private static uint Checksum(byte[] bytes)
    {
        uint result = 0;
        foreach (var value in bytes) { result ^= (uint)value << 24; for (var bit = 0; bit < 8; bit++) result = (result & 0x80000000) != 0 ? result << 1 ^ 0x04c11db7u : result << 1; }
        return result;
    }
}
