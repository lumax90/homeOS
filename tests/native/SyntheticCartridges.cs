using System.Buffers.Binary;
using System.Text;

namespace Home.NativeTests;

// Original CPU diagnostic programs, not games or acquired ROM/BIOS data. Never publish in a Home catalogue.
public static class SyntheticCartridges
{
    public static byte[] Create(string system)=>system switch{"gb"=>GameBoy(false),"gbc"=>GameBoy(true),"nes"=>Nes(),"snes"=>Snes(),"megadrive"=>MegaDrive(),"ps1"=>PlayStation(),_=>throw new ArgumentException("Original cartridge fixture unavailable.")};
    private static byte[] GameBoy(bool color)
    {
        var rom=new byte[32768];rom[0x100]=0xc3;rom[0x101]=0x50;rom[0x102]=1;
        // mGBA 0.10.5's GBIsROM only examines this four-byte format signature. No complete logo/BIOS data is included.
        new byte[]{0xce,0xed,0x66,0x66}.CopyTo(rom,0x104);Encoding.ASCII.GetBytes("HOME ORIGINAL").CopyTo(rom,0x134);rom[0x143]=color?(byte)0x80:(byte)0;rom[0x147]=3;rom[0x149]=2;
        var code=new Code(0x150);code.Emit(0xf3,0x31,0xfe,0xff,0x3e,0,0xe0,0x40,0x3e,0x0a,0xea,0,0,0x3e,0x5a,0xea,0,0xa0,0x21,0,0x80,0x01,0,0x20);
        code.Label("clear");code.Emit(0x36,0,0x23,0x0b,0x78,0xb1);code.Rel(0x20,"clear");
        if(color)code.Emit(0x3e,0x80,0xe0,0x68,0x3e,0xe0,0xe0,0x69,0x3e,3,0xe0,0x69);
        else code.Emit(0x3e,0,0xe0,0x47);
        code.Emit(0x3e,0x91,0xe0,0x40);code.Label("loop");code.Emit(0xf0,0x44,0xfe,144);code.Rel(0x38,"loop");code.Emit(0x3e,0x10,0xe0,0,0xf0,0,0xf0,0,0xe6,1);code.Rel(0x20,"released");
        if(color)code.Emit(0x3e,0x80,0xe0,0x68,0x3e,0x1f,0xe0,0x69,0x3e,0,0xe0,0x69);
        else code.Emit(0x3e,3,0xe0,0x47);
        code.Rel(0x18,"wait");code.Label("released");
        if(color)code.Emit(0x3e,0x80,0xe0,0x68,0x3e,0xe0,0xe0,0x69,0x3e,3,0xe0,0x69);
        else code.Emit(0x3e,0,0xe0,0x47);
        code.Label("wait");code.Emit(0xf0,0x44,0xfe,144);code.Rel(0x30,"wait");code.Rel(0x18,"loop");code.Finish().CopyTo(rom,0x150);
        byte check=0;for(var i=0x134;i<=0x14c;i++)check=(byte)(check-rom[i]-1);rom[0x14d]=check;return rom;
    }
    private static byte[] Nes()
    {
        var rom=new byte[16+16384+8192];"NES\x1a"u8.CopyTo(rom);rom[4]=1;rom[5]=1;rom[6]=2;
        var code=new Code(0x8000);code.Emit(0x78,0xd8,0xa2,0xff,0x9a,0xa9,0,0x8d,0,0x20,0x8d,1,0x20,0xa9,0x5a,0x8d,0,0x60);
        code.Emit(0xa9,1,0x8d,0x15,0x40,0xa9,0xbf,0x8d,0,0x40,0xa9,0x80,0x8d,2,0x40,0xa9,8,0x8d,3,0x40);
        code.Label("frame");code.Emit(0x2c,2,0x20);code.Rel(0x10,"frame");code.Emit(0xa9,1,0x8d,0x16,0x40,0xa9,0,0x8d,0x16,0x40,0xad,0x16,0x40,0x29,1);code.Rel(0xf0,"released");code.Emit(0xa2,0x16);code.Rel(0xd0,"palette");code.Label("released");code.Emit(0xa2,0x1a);code.Label("palette");code.Emit(0xa9,0x3f,0x8d,6,0x20,0xa9,0,0x8d,6,0x20,0x8e,7,0x20,0xa9,8,0x8d,1,0x20);code.Absolute(0x4c,"frame");code.Finish().CopyTo(rom,16);
        for(var i=0;i<3;i++)BinaryPrimitives.WriteUInt16LittleEndian(rom.AsSpan(16+16384-6+i*2),0x8000);return rom;
    }
    private static byte[] Snes()
    {
        var rom=new byte[32768];var code=new Code(0x8000);code.Emit(0x78,0x18,0xfb,0xc2,0x10,0xa2,0xff,0x1f,0x9a,0xe2,0x20,0xa9,0x80,0x8d,0,0x21,0xa9,0,0x8d,0x2c,0x21,0xa9,1,0x8d,0,0x42,0xa9,0x5a,0x8f,0,0,0x70,0xa9,15,0x8d,0,0x21);
        code.Label("frame");code.Emit(0xad,0x10,0x42);code.Rel(0x10,"frame");code.Label("joy");code.Emit(0xad,0x12,0x42,0x29,1);code.Rel(0xd0,"joy");code.Emit(0xa9,0,0x8d,0x21,0x21,0xad,0x18,0x42,0x29,0x80);code.Rel(0xf0,"released");code.Emit(0xa9,0x1f,0x8d,0x22,0x21,0xa9,0);code.Rel(0x80,"high");code.Label("released");code.Emit(0xa9,0xe0,0x8d,0x22,0x21,0xa9,3);code.Label("high");code.Emit(0x8d,0x22,0x21);code.Absolute(0x4c,"frame");code.Finish().CopyTo(rom,0);
        Encoding.ASCII.GetBytes("HOME ORIGINAL TEST   ").CopyTo(rom,0x7fc0);rom[0x7fd5]=0x20;rom[0x7fd6]=2;rom[0x7fd7]=5;rom[0x7fd8]=1;rom[0x7fd9]=1;rom[0x7fda]=0x33;BinaryPrimitives.WriteUInt16LittleEndian(rom.AsSpan(0x7fdc),0xffff);BinaryPrimitives.WriteUInt16LittleEndian(rom.AsSpan(0x7ffc),0x8000);return rom;
    }
    private static byte[] MegaDrive()
    {
        var rom=new byte[16384];void Long(int at,uint value)=>BinaryPrimitives.WriteUInt32BigEndian(rom.AsSpan(at),value);
        Long(0,0x00ffff00);Long(4,0x200);Encoding.ASCII.GetBytes("SEGA MEGA DRIVE ").CopyTo(rom,0x100);Encoding.ASCII.GetBytes("(C)HOME ORIGINAL ").CopyTo(rom,0x110);Encoding.ASCII.GetBytes("ORIGINAL DIAGNOSTIC ONLY").CopyTo(rom,0x120);Encoding.ASCII.GetBytes("J").CopyTo(rom,0x190);Long(0x1a4,16383);Long(0x1a8,0xff0000);Long(0x1ac,0xffffff);rom[0x1b0]=(byte)'R';rom[0x1b1]=(byte)'A';rom[0x1b2]=0xf8;rom[0x1b3]=0x20;Long(0x1b4,0x200001);Long(0x1b8,0x2001ff);Encoding.ASCII.GetBytes("U               ").CopyTo(rom,0x1f0);
        var words=new List<ushort>();void Word(ushort value)=>words.Add(value);void Address(uint value){Word((ushort)(value>>16));Word((ushort)value);}void ByteMove(byte value,uint address){Word(0x13fc);Word(value);Address(address);}void Vdp(ushort value){Word(0x33fc);Word(value);Address(0xc00004);}
        Word(0x46fc);Word(0x2700);ByteMove((byte)'S',0xa14000);ByteMove((byte)'E',0xa14001);ByteMove((byte)'G',0xa14002);ByteMove((byte)'A',0xa14003);ByteMove(1,0xa130f1);ByteMove(0x5a,0x200001);ByteMove(0x40,0xa10009);ByteMove(0x40,0xa10003);
        foreach(var value in new ushort[]{0x8004,0x8144,0x8230,0x8407,0x857c,0x8700,0x8b00,0x8c81,0x8d3f,0x8f02,0x9011})Vdp(value);
        var loop=words.Count;Word(0x1039);Address(0xa10003);Word(0x0800);Word(5);Word(0x6700);var redOffset=words.Count;Word(0);Word(0x303c);Word(0x00e0);Word(0x6000);var colorOffset=words.Count;Word(0);var red=words.Count;Word(0x303c);Word(0x000e);var color=words.Count;Word(0x23fc);Address(0xc0000000);Address(0xc00004);Word(0x33c0);Address(0xc00000);Word(0x6000);var loopOffset=words.Count;Word(0);
        words[redOffset]=(ushort)((red-redOffset)*2);words[colorOffset]=(ushort)((color-colorOffset)*2);words[loopOffset]=unchecked((ushort)((loop-loopOffset)*2));for(var i=0;i<words.Count;i++)BinaryPrimitives.WriteUInt16BigEndian(rom.AsSpan(0x200+i*2),words[i]);return rom;
    }
    private static byte[] PlayStation()
    {
        // Original MIPS PS-X program for the diagnostic harness only, NOT a Windows EXE/importer exception.
        const uint origin=0x80010000;var words=new List<uint>();var labels=new Dictionary<string,int>();var fixes=new List<(int At,string Label,bool Jump)>();
        void Emit(uint word)=>words.Add(word);void Label(string label)=>labels.Add(label,words.Count);
        void Load(int register,uint value){Emit(0x3c000000u|((uint)register<<16)|(value>>16));Emit(0x34000000u|((uint)register<<21)|((uint)register<<16)|(value&65535));}
        void Jump(string label,bool link=false){fixes.Add((words.Count,label,true));Emit(link?0x0c000000u:0x08000000u);Emit(0);}
        void Branch(uint instruction,string label){fixes.Add((words.Count,label,false));Emit(instruction);Emit(0);}
        Load(8,0x1f801814);foreach(var command in new uint[]{0x00000000,0x03000000,0x08000001,0x05000000,0x06c60260,0x07042018}){Load(9,command);Emit(0xad090000);}
        Load(8,0x1f801040);Load(9,0x88);Emit(0xa509000e);Load(9,0x0d);Emit(0xa5090008);Load(16,0x80001000);
        Label("loop");Emit(0x8e110000);Emit(0);Emit(0x26310001);Emit(0xae110000);Load(9,0x40);Emit(0xa509000a);Load(9,3);Emit(0xa509000a);
        foreach(var value in new byte[]{1,0x42,0,0,0}){Load(9,value);Jump("exchange",true);}
        Emit(0x304a0020);Load(9,0);Emit(0xa509000a);Branch(0x11400000,"red");Load(9,0x0200ff00);Jump("paint");Label("red");Load(9,0x020000ff);
        Label("paint");Load(11,0x1f801810);Emit(0xad690000);Emit(0xad600000);Load(9,0x00f00140);Emit(0xad690000);Jump("loop");
        Label("exchange");Emit(0xa1090000);Label("receive");Emit(0x950c0004);Emit(0x318c0002);Branch(0x11800000,"receive");Emit(0x91020000);Emit(0x03e00008);Emit(0);
        foreach(var fix in fixes){var destination=origin+(uint)labels[fix.Label]*4;words[fix.At]|=fix.Jump?(destination>>2)&0x03ffffff:unchecked((ushort)(labels[fix.Label]-fix.At-1));}
        var rom=new byte[4096];"PS-X EXE"u8.CopyTo(rom);BinaryPrimitives.WriteUInt32LittleEndian(rom.AsSpan(0x10),origin);BinaryPrimitives.WriteUInt32LittleEndian(rom.AsSpan(0x18),origin);BinaryPrimitives.WriteUInt32LittleEndian(rom.AsSpan(0x1c),2048);BinaryPrimitives.WriteUInt32LittleEndian(rom.AsSpan(0x30),0x801fff00);
        Encoding.ASCII.GetBytes("ORIGINAL HOME DIAGNOSTIC; NO GAME OR BIOS DATA").CopyTo(rom,0x4c);for(var i=0;i<words.Count;i++)BinaryPrimitives.WriteUInt32LittleEndian(rom.AsSpan(2048+i*4),words[i]);return rom;
    }
    private sealed class Code(int origin)
    {
        private readonly List<byte> _code=[];private readonly Dictionary<string,int> _labels=[];private readonly List<(int At,string Label,bool Absolute)> _fixes=[];
        public void Emit(params byte[] bytes)=>_code.AddRange(bytes);public void Label(string label)=>_labels.Add(label,_code.Count);
        public void Rel(byte op,string label){Emit(op,0);_fixes.Add((_code.Count-1,label,false));}
        public void Absolute(byte op,string label){Emit(op,0,0);_fixes.Add((_code.Count-2,label,true));}
        public byte[] Finish(){foreach(var fix in _fixes){if(fix.Absolute){var target=origin+_labels[fix.Label];_code[fix.At]=(byte)target;_code[fix.At+1]=(byte)(target>>8);}else{var delta=_labels[fix.Label]-fix.At-1;if(delta is <-128 or >127)throw new InvalidDataException("Original fixture branch is out of range.");_code[fix.At]=unchecked((byte)(sbyte)delta);}}return _code.ToArray();}
    }
}
