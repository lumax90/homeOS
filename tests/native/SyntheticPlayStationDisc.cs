using System.Buffers.Binary;
using System.Text;

namespace Home.NativeTests;

/// <summary>Original ISO9660 wrapper around our MIPS diagnostic. No game, BIOS or console logo data.</summary>
public static class SyntheticPlayStationDisc
{
    public static byte[] Create()
    {
        const int sector=2048,total=128,root=20,config=21,program=22;
        var image=new byte[total*sector];var executable=SyntheticCartridges.Create("ps1");
        var boot=Encoding.ASCII.GetBytes("BOOT = cdrom:\\HOME.EXE;1\r\nTCB = 4\r\nEVENT = 10\r\nSTACK = 801fff00\r\n");
        void Both16(int at,ushort value){BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(at),value);BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(at+2),value);}
        void Both32(int at,uint value){BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(at),value);BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(at+4),value);}
        int Record(int at,byte[] name,int block,int bytes,bool folder=false)
        {
            var length=33+name.Length+(name.Length%2==0?1:0);image[at]=(byte)length;Both32(at+2,(uint)block);Both32(at+10,(uint)bytes);new byte[]{126,10,22,0,0,0,0}.CopyTo(image,at+18);image[at+25]=folder?(byte)2:(byte)0;Both16(at+28,1);image[at+32]=(byte)name.Length;name.CopyTo(image,at+33);return length;
        }
        var pvd=16*sector;image[pvd]=1;"CD001"u8.CopyTo(image.AsSpan(pvd+1));image[pvd+6]=1;
        Encoding.ASCII.GetBytes("PLAYSTATION".PadRight(32)).CopyTo(image,pvd+8);Encoding.ASCII.GetBytes("HOME_ORIGINAL_DIAGNOSTIC".PadRight(32)).CopyTo(image,pvd+40);
        Both32(pvd+80,total);Both16(pvd+120,1);Both16(pvd+124,1);Both16(pvd+128,sector);Both32(pvd+132,10);BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(pvd+140),18);BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(pvd+148),19);Record(pvd+156,[0],root,sector,true);image[pvd+881]=1;
        image[17*sector]=255;"CD001"u8.CopyTo(image.AsSpan(17*sector+1));image[17*sector+6]=1;
        image[18*sector]=1;BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(18*sector+2),root);BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(18*sector+6),1);
        image[19*sector]=1;BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(19*sector+2),root);BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(19*sector+6),1);
        var offset=root*sector;offset+=Record(offset,[0],root,sector,true);offset+=Record(offset,[1],root,sector,true);offset+=Record(offset,Encoding.ASCII.GetBytes("SYSTEM.CNF;1"),config,boot.Length);Record(offset,Encoding.ASCII.GetBytes("HOME.EXE;1"),program,executable.Length);
        boot.CopyTo(image,config*sector);executable.CopyTo(image,program*sector);return image;
    }
}
