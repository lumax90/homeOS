using System.Buffers.Binary;
using System.Net.Sockets;
using HomeServer.Core;

namespace HomeServer.Gaming;

/// <summary>RetroArch 1.22.2 Windows x64 Remote RetroPad ABI; a protocol adapter, not a virtual HID driver.</summary>
public static class RetroPadProtocol
{
    public const int PacketBytes = 20;
    public const ushort GbaButtons = (1 << 0) | (1 << 2) | (1 << 3) | (1 << 4) | (1 << 5) | (1 << 6) | (1 << 7) | (1 << 8) | (1 << 10) | (1 << 11);
    public static byte[] Button(int id,bool pressed,ushort allowed=GbaButtons)
    {
        if(id is <0 or >15||(allowed&(1<<id))==0)throw new DomainException("Oyun tuşu geçersiz.");
        var packet = new byte[PacketBytes];
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), 1); // RETRO_DEVICE_JOYPAD.
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(12), id);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(16), pressed ? (ushort)1 : (ushort)0);
        return packet;
    }
    // Upstream's fixed receiver ignores malformed packets; neutral input must use valid releases.
    public static IEnumerable<byte[]> ReleaseAll(ushort allowed=GbaButtons)
    {
        for(var id=0;id<16;id++)if((allowed&(1<<id))!=0)yield return Button(id,false,allowed);
    }
    public static async Task ReleaseAllAsync(UdpClient pad, CancellationToken token)
    {
        foreach (var packet in ReleaseAll()) await pad.SendAsync(packet, token);
    }
    public static IEnumerable<byte[]> Changes(ushort previous,ushort desired,ushort allowed=GbaButtons)
    {
        Validate(previous,allowed);Validate(desired,allowed);
        for(var id=0;id<16;id++)if((previous&~desired&(1<<id))!=0)yield return Button(id,false,allowed);
        for(var id=0;id<16;id++)if((desired&~previous&(1<<id))!=0)yield return Button(id,true,allowed);
    }
    public static void Validate(ushort buttons,ushort allowed=GbaButtons){if((buttons&~allowed)!=0)throw new DomainException("Oyun tuşları geçersiz.");}
    public static byte[] Analog(int index,int axis,short value)
    {
        if(index is <0 or >2||axis<0||axis>(index==2?15:1))throw new DomainException("Oyun analog girdisi geçersiz.");var packet=new byte[PacketBytes];BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4),5);BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8),index);BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(12),axis);BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(16),value);return packet;
    }
    public static IEnumerable<byte[]> AnalogChanges(NativePadFrame previous,NativePadFrame next)
    {if(previous.LeftX!=next.LeftX)yield return Analog(0,0,next.LeftX);if(previous.LeftY!=next.LeftY)yield return Analog(0,1,next.LeftY);if(previous.RightX!=next.RightX)yield return Analog(1,0,next.RightX);if(previous.RightY!=next.RightY)yield return Analog(1,1,next.RightY);if(previous.Pressure!=next.Pressure){var a=(previous.Pressure??"").Split(',',StringSplitOptions.RemoveEmptyEntries);var b=(next.Pressure??"").Split(',',StringSplitOptions.RemoveEmptyEntries);for(var i=0;i<16;i++){var left=i<a.Length?short.Parse(a[i]):(short)0;var right=i<b.Length?short.Parse(b[i]):(short)0;if(left!=right)yield return Analog(2,i,right);}}}
    public static byte[] Peripheral(int device,int id,short value,int index=0)
    {
        var max=device switch{2=>11,3=>323,4=>17,6=>4,_=>0};if(id<0||id>=max||index!=0)throw new DomainException("Oyun çevre birimi girdisi geçersiz.");var packet=new byte[PacketBytes];BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4),device);BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8),index);BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(12),id);BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(16),value);return packet;
    }
    public static IEnumerable<byte[]> PeripheralChanges(NativePadFrame previous,NativePadFrame next,bool release=false)
    {
        if(previous.PointerX!=next.PointerX){yield return Peripheral(6,0,next.PointerX);yield return Peripheral(4,13,next.PointerX);}
        if(previous.PointerY!=next.PointerY){yield return Peripheral(6,1,next.PointerY);yield return Peripheral(4,14,next.PointerY);}
        if(!release&&previous.MotionX!=next.MotionX)yield return Peripheral(2,0,(short)Math.Clamp((long)next.MotionX-previous.MotionX,short.MinValue,short.MaxValue));
        if(!release&&previous.MotionY!=next.MotionY)yield return Peripheral(2,1,(short)Math.Clamp((long)next.MotionY-previous.MotionY,short.MinValue,short.MaxValue));
        if(!release&&previous.Wheel!=next.Wheel)yield return Peripheral(2,next.Wheel>previous.Wheel?5:4,1);
        if(previous.Offscreen!=next.Offscreen)yield return Peripheral(4,15,next.Offscreen?(short)1:(short)0);
        if(previous.Pressed!=next.Pressed){yield return Peripheral(6,2,next.Pressed?(short)1:(short)0);yield return Peripheral(6,3,next.Pressed?(short)1:(short)0);yield return Peripheral(2,2,next.Pressed?(short)1:(short)0);yield return Peripheral(4,2,next.Pressed?(short)1:(short)0);}
        if((previous.MouseButtons&2)!=(next.MouseButtons&2))yield return Peripheral(2,3,(next.MouseButtons&2)!=0?(short)1:(short)0);
        if((previous.MouseButtons&4)!=(next.MouseButtons&4))yield return Peripheral(2,6,(next.MouseButtons&4)!=0?(short)1:(short)0);
        if(previous.Keyboard!=next.Keyboard){var a=(previous.Keyboard??"").Split(',',StringSplitOptions.RemoveEmptyEntries);var b=(next.Keyboard??"").Split(',',StringSplitOptions.RemoveEmptyEntries);foreach(var key in a.Except(b))yield return Peripheral(3,int.Parse(key),0);foreach(var key in b.Except(a).Select(int.Parse).OrderByDescending(key=>key is >=303 and <=308))yield return Peripheral(3,key,1);}
    }
}
