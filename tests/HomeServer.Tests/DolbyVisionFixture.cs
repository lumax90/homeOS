using System.Buffers.Binary;
using System.Text;
using HomeServer.Infrastructure;

namespace HomeServer.Tests;

/// <summary>Original, isolated generated DV P8.1 fixture. No provider/user film or downloaded media.</summary>
internal static class DolbyVisionFixture
{
    // One constant RPU NAL generated locally with MIT dovi_tool 2.3.4, profile 8.1,
    // original 1000-nit/400-FALL contract. Repeated per frame; not a P5/P7 colour fixture.
    private const string Rpu="fAEZCAkIQGE2UG8AP/gB/8AP/9AAAAgAAAaAAABAAAA0AAADAgAAAwGiVmAAA16iVm+fzrHCVmRMoAAAEAAAAwCAAAADAIAAAAMBw2IkMBhgpeMI4FFAAAAaY+Wv//AAAAMAAAMAAAMABgIA+A4VMBAKAAADAAADAAADACQYD6AABA+gBkCgMBwAQAQAAgkAf8AARQsBAAADAIiI+l6A";
    public static async Task<string> GenerateAsync(TestWorkspace home,string ffmpeg,string audio="truehd")
    {
        var elementary=Path.Combine(home.Root,"original-base.hevc");
        await MediaProcess.RunAsync(ffmpeg,["-hide_banner","-loglevel","error","-nostdin","-f","lavfi","-i","testsrc2=size=128x72:rate=24","-t","4",
            "-c:v","libx265","-preset","ultrafast","-pix_fmt","yuv420p10le","-x265-params","pools=1:frame-threads=1:log-level=error:bframes=0:colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc","-f","hevc",elementary],TimeSpan.FromSeconds(30),CancellationToken.None);
        var data=await File.ReadAllBytesAsync(elementary);var offsets=new List<(int Offset,int Prefix)>();
        for(var at=0;at+4<data.Length;at++)
        {
            if(data[at]!=0||data[at+1]!=0)continue;
            var prefix=data[at+2]==1?3:data[at+2]==0&&data[at+3]==1?4:0;
            if(prefix>0){offsets.Add((at,prefix));at+=prefix-1;}
        }
        var interleaved=Path.Combine(home.Root,"original-dv.hevc");
        await using(var output=File.Create(interleaved))
        {
            for(var i=0;i<offsets.Count;i++)
            {
                var (at,prefix)=offsets[i];var end=i+1<offsets.Count?offsets[i+1].Offset:data.Length;
                await output.WriteAsync(data.AsMemory(at,end-at));
                if(((data[at+prefix]>>1)&63)<=31){await output.WriteAsync(new byte[]{0,0,0,1});await output.WriteAsync(Convert.FromBase64String(Rpu));}
            }
        }
        var file=Path.Combine(home.Source,"ORIGINAL isolated Dolby Vision P8.1.mp4");
        await MediaProcess.RunAsync(ffmpeg,["-v","error","-nostdin","-r","24","-i",interleaved,"-f","lavfi","-i","sine=frequency=440:sample_rate=48000",
            "-t","4","-c:v","copy","-tag:v","hvc1","-c:a",audio,"-ac",audio=="truehd"?"6":"2",
            "-channel_layout:a",audio=="truehd"?"5.1(side)":"stereo","-strict","-2",file],TimeSpan.FromSeconds(30),CancellationToken.None);
        var mp4=await File.ReadAllBytesAsync(file);
        // Annex-B has real RPU data, but raw demux does not supply an MP4 DOVI
        // configuration. Add the corresponding original P8.1 record, not fake pixels.
        Assert.True(FindAtom(mp4,"moov")>FindAtom(mp4,"mdat"));
        await File.WriteAllBytesAsync(file,RewriteAtoms(mp4));return file;
    }
    private static int FindAtom(byte[] data,string type)
    {
        for(var at=0;at+8<=data.Length;){var length=checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at,4)));if(Encoding.ASCII.GetString(data,at+4,4)==type)return at;if(length<8)break;at+=length;}
        return -1;
    }
    private static byte[] RewriteAtoms(byte[] data,int start=0)
    {
        using var output=new MemoryStream();output.Write(data,0,start);
        for(var at=start;at<data.Length;)
        {
            var length=checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at,4)));Assert.True(length>=8&&at+length<=data.Length);
            var type=Encoding.ASCII.GetString(data,at+4,4);var box=data.AsSpan(at,length).ToArray();
            var nested=type switch{"moov" or "trak" or "mdia" or "minf" or "stbl"=>8,"stsd"=>16,"hvc1" or "hev1"=>86,_=>0};
            if(nested>0)
            {
                box=RewriteAtoms(box,nested);
                if(type is "hvc1" or "hev1")
                {
                    var config=new byte[32];BinaryPrimitives.WriteUInt32BigEndian(config,32);Encoding.ASCII.GetBytes("dvvC").CopyTo(config,4);
                    config[8]=1;config[10]=8<<1;config[11]=(1<<3)|5;config[12]=1<<4;box=[..box,..config];
                }
                BinaryPrimitives.WriteUInt32BigEndian(box,(uint)box.Length);
            }
            output.Write(box);at+=length;
        }
        return output.ToArray();
    }
}
