using System.Buffers.Binary;

namespace HomeServer.Subtitles;

public static class MovieHash
{
    public static async Task<string?> ComputeAsync(Stream stream, CancellationToken token)
    {
        if (!stream.CanSeek || stream.Length < 131072) return null;
        var buffer = new byte[65536];
        ulong sum = (ulong)stream.Length;
        for (var part = 0; part < 2; part++)
        {
            stream.Seek(part == 0 ? 0 : stream.Length - buffer.Length, SeekOrigin.Begin);
            await stream.ReadExactlyAsync(buffer, token);
            for (var index = 0; index < buffer.Length; index += 8) sum = unchecked(sum + BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(index, 8)));
        }
        return sum.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }
}
