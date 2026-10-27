using HomeServer.Core;

namespace HomeServer.Gaming;

public sealed partial class NativeRomLibrary
{
    public static async Task<string?> IdentifyIsoAsync(string path,CancellationToken token)
    {
        await using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous);
        var prefix=new byte[(int)Math.Min(file.Length,NativeRomDetection.PrefixBytes)];await file.ReadExactlyAsync(prefix,token);
        var samples=new List<(long Offset,byte[] Data)>();var total=prefix.Length;
        for(var round=0;round<4;round++)
        {
            var reads=new List<NativeRomProbeRead>();var result=NativeDiscDetection.Identify(new("game.iso",file.Length),prefix,samples,reads);
            if(result is not null)return result;if(reads.Count==0)return null;
            foreach(var read in reads.Distinct())
            {
                if(read.Length<1||read.Offset<0||read.Offset>file.Length-read.Length||total+read.Length>NativeRomDetection.MaximumSampleBytes)throw new DomainException("Disk algılama sınırı aşıldı.",409);
                file.Position=read.Offset;var bytes=new byte[read.Length];await file.ReadExactlyAsync(bytes,token);samples.Add((read.Offset,bytes));total+=bytes.Length;
            }
        }
        return null;
    }
    private static async Task ValidateIsoSystemAsync(string path,string selected,CancellationToken token)
    {
        var actual=await IdentifyIsoAsync(path,token);
        if(actual is not null&&actual!=selected&&actual is "psp" or "ps1" or "ps2")
            throw new DomainException("Diskin algılanan sistemi seçtiğin konsolla eşleşmiyor. Doğru konsolu seç.",400);
    }
}
