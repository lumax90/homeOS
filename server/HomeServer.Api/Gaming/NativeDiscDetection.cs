using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace HomeServer.Gaming;

/// <summary>Read-only ISO9660 boot-file detection. Requests bounded ranges only from the user's selected file.</summary>
internal static class NativeDiscDetection
{
    public static string? Identify(NativeRomProbeFile file,byte[] prefix,List<(long Offset,byte[] Data)> samples,List<NativeRomProbeRead> requests)
    {
        foreach(var (stride,payloadOffset) in new[]{(2048,0),(2352,24),(2352,16)})
        {
            var pvd=Read(16L*stride+payloadOffset,2048,false);if(pvd is null||pvd[0]!=1||!pvd.AsSpan(1,5).SequenceEqual("CD001"u8)||pvd[6]!=1||BinaryPrimitives.ReadUInt16LittleEndian(pvd.AsSpan(128))!=2048)continue;
            var root=pvd.AsSpan(156);if(root[0]<34)continue;var extent=BinaryPrimitives.ReadUInt32LittleEndian(root[2..]);var bytes=BinaryPrimitives.ReadUInt32LittleEndian(root[10..]);
            if(bytes is <34 or >32768)return null;var directory=Logical(extent,(int)bytes);if(directory is null)return null;
            for(var at=0;at<directory.Length;)
            {
                var length=directory[at];if(length==0){at=(at/2048+1)*2048;continue;}if(length<34||at+length>directory.Length)return null;var record=directory.AsSpan(at,length);var nameLength=record[32];
                if(nameLength>0&&33+nameLength<=length&&(record[25]&2)==0&&Encoding.ASCII.GetString(record.Slice(33,nameLength)).Equals("SYSTEM.CNF;1",StringComparison.OrdinalIgnoreCase))
                {
                    var block=BinaryPrimitives.ReadUInt32LittleEndian(record[2..]);var count=BinaryPrimitives.ReadUInt32LittleEndian(record[10..]);if(count is <1 or >16384)return null;var boot=Logical(block,(int)count);if(boot is null)return null;var text=Encoding.ASCII.GetString(boot).TrimStart('\0','\ufeff');
                    if(Regex.IsMatch(text,@"(?im)^\s*BOOT2\s*=\s*cdrom0?:",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)))return "ps2";
                    if(Regex.IsMatch(text,@"(?im)^\s*BOOT\s*=\s*cdrom0?:",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)))return "ps1";return null;
                }
                at+=length;
            }
            return null;

            byte[]? Logical(uint block,int count)
            {
                var offset=(long)block*stride;var rawLength=((count+2047)/2048-1)*stride+payloadOffset+Math.Min(count,2048);if(count>2048)rawLength=((count+2047)/2048-1)*stride+payloadOffset+(count%2048==0?2048:count%2048);
                var raw=Read(offset,rawLength,true);if(raw is null)return null;var data=new byte[count];for(var i=0;i<count;i+=2048)raw.AsSpan((i/2048)*stride+payloadOffset,Math.Min(2048,count-i)).CopyTo(data.AsSpan(i));return data;
            }
        }
        return null;
        byte[]? Read(long offset,int count,bool request)
        {
            if(offset<0||count is <1 or >65536||offset>file.Bytes-count)return null;
            if(offset+count<=prefix.Length)return prefix.AsSpan((int)offset,count).ToArray();
            foreach(var sample in samples)if(offset>=sample.Offset&&offset+count<=sample.Offset+sample.Data.Length)return sample.Data.AsSpan((int)(offset-sample.Offset),count).ToArray();
            if(request&&requests.Count<4)requests.Add(new(file.Name,offset,count));return null;
        }
    }
}
