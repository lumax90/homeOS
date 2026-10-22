namespace HomeServer.Usenet;

public sealed class Crc32
{
    private static readonly uint[] Table = Enumerable.Range(0, 256).Select(index =>
    {
        var crc = (uint)index; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u); return crc;
    }).ToArray();
    private uint _value = uint.MaxValue;
    public uint Value => ~_value;
    public void Append(ReadOnlySpan<byte> bytes) { foreach (var value in bytes) _value = Table[(_value ^ value) & 255] ^ (_value >> 8); }
    public static uint Hash(ReadOnlySpan<byte> bytes) { var crc = new Crc32(); crc.Append(bytes); return crc.Value; }
}
