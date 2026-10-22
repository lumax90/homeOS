using System.Globalization;
using System.Text;
using HomeServer.Core;

namespace HomeServer.Usenet;

public static class YencDecoder
{
    public static YencPart Decode(IEnumerable<byte[]> lines, long maximumFileSize)
    {
        string? name = null; long size = 0, offset = 0, end = 0; var number = 1; var multipart = false; var partFound = false; var complete = false;
        using var output = new MemoryStream(); uint? expected = null, fileCrc = null;
        foreach (var raw in lines)
        {
            var line = Encoding.Latin1.GetString(raw);
            if (name is null)
            {
                if (!line.StartsWith("=ybegin ", StringComparison.Ordinal)) continue;
                var start = line.IndexOf(" name=", StringComparison.Ordinal);
                if (start < 0) throw new DomainException("yEnc dosya adı bulunamadı.");
                var fields = Fields(line[..start]);
                name = NzbParser.SafeName(DecodeName(raw.AsSpan(start + 6)));
                size = Number(fields, "size"); if (size is < 1 || size > maximumFileSize) throw new DomainException("yEnc dosya boyutu izin verilen sınırı aşıyor.");
                multipart = fields.ContainsKey("part"); number = multipart ? checked((int)Number(fields, "part")) : 1; end = size;
                continue;
            }
            if (line.StartsWith("=ypart ", StringComparison.Ordinal))
            {
                if (!multipart || partFound || output.Length > 0) throw new DomainException("yEnc parça başlığı beklenmeyen konumda.");
                var fields = Fields(line); offset = Number(fields, "begin") - 1; end = Number(fields, "end"); partFound = true;
                if (offset < 0 || end <= offset || end > size) throw new DomainException("yEnc parça aralığı geçersiz.");
                continue;
            }
            if (line.StartsWith("=yend ", StringComparison.Ordinal))
            {
                var fields = Fields(line);
                if (Number(fields, "size") != output.Length || output.Length != end - offset || multipart && !partFound) throw new DomainException("yEnc parça boyutu eşleşmedi.");
                if (fields.ContainsKey("part") && Number(fields, "part") != number) throw new DomainException("yEnc parça numarası eşleşmedi.");
                if (fields.TryGetValue("pcrc32", out var partCrc)) expected = ParseCrc(partCrc);
                if (fields.TryGetValue("crc32", out var whole)) fileCrc = ParseCrc(whole);
                expected ??= !multipart ? fileCrc : null;
                if (expected is null) throw new DomainException("yEnc parçası CRC doğrulaması içermiyor.");
                complete = true; break;
            }
            if (multipart && !partFound) throw new DomainException("yEnc parça aralığı eksik.");
            for (var index = 0; index < raw.Length; index++)
            {
                var value = raw[index];
                if (value == '=') { if (++index >= raw.Length) throw new DomainException("yEnc escape dizisi kesilmiş."); value = unchecked((byte)(raw[index] - 64)); }
                output.WriteByte(unchecked((byte)(value - 42)));
            }
            if (output.Length > 16 * 1024 * 1024 || output.Length > end - offset) throw new DomainException("yEnc parçası izin verilen boyutu aşıyor.");
        }
        if (name is null || !complete) throw new DomainException("Article tam bir yEnc parçası içermiyor.");
        var payload = output.ToArray(); var crc = Crc32.Hash(payload);
        if (expected != crc) throw new DomainException("yEnc CRC eşleşmedi; bozuk parça yayımlanmadı.");
        return new YencPart(name, size, offset, number, payload, crc, fileCrc);
    }
    private static Dictionary<string, string> Fields(string line)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var word in line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var equals = word.IndexOf('='); if (equals < 1 || !result.TryAdd(word[..equals], word[(equals + 1)..])) throw new DomainException("yEnc başlığı geçersiz.");
        }
        return result;
    }
    private static long Number(Dictionary<string, string> fields, string key) => fields.TryGetValue(key, out var value) && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
        ? number : throw new DomainException("yEnc sayısal alanı geçersiz.");
    private static uint ParseCrc(string value) => value.Length == 8 && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var crc) ? crc : throw new DomainException("yEnc CRC alanı geçersiz.");
    private static string DecodeName(ReadOnlySpan<byte> bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes); } catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }
}
