using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Home.NativeTests;

/// <summary>Original clean-room ARM test fixture, not a distributed game/BIOS. Isolated native tests only.</summary>
public static class SyntheticGba
{
    public static byte[] Create(bool latencyPixelOnly = false)
    {
        var asm = new ArmFixture();
        asm.Halfword(0x04000000, latencyPixelOnly?0x0404u:0x0403u); // Palette-only latency variant; full-paint Mode 3 fixture remains unchanged.
        asm.Halfword(0x04000084, 0x0080); // Master sound.
        asm.Halfword(0x04000080, 0x1177); // PSG channel 1, both speakers.
        asm.Halfword(0x04000082, 0x0002);
        asm.Halfword(0x04000062, 0xf080);
        asm.Halfword(0x04000064, 0x8700); // Original square-wave test tone.
        asm.Move(5, 0, 0xe); asm.Move(6, 0, 0xe); // Sample frames and consecutive A-held frames.
        asm.Label("loop");
        asm.Literal(0, 0x04000006);
        asm.Label("endBlank"); asm.Emit(0xe1d010b0); asm.Emit(0xe35100a0); asm.Branch("endBlank", 0xa);
        asm.Label("beginBlank"); asm.Emit(0xe1d010b0); asm.Emit(0xe35100a0); asm.Branch("beginBlank", 0xb);
        asm.Literal(0, 0x04000130); asm.Emit(0xe1d010b0); asm.Emit(0xe3110001);
        asm.Move(3, 0x001f, 0); // Red when A is pressed (active-low key register).
        asm.Move(3, 0x03e0, 1); // Green when released.
        asm.Emit(0xe2855001); asm.Emit(0x02866001); asm.Emit(0x13a06000); // ADD frame; ADDEQ held; MOVNE held,0.
        asm.Literal(0, 0x02000000); asm.Emit(0xe1c010b0); asm.Emit(0xe5805004); asm.Emit(0xe5806008);
        // Original fixture diagnostic memory: KEYINPUT @+0, sample count @+4, consecutive A-held count @+8.
        asm.Literal(2, 0x06000000);
        if (latencyPixelOnly)
        {
            // Mode 4 VRAM starts at index zero. One palette write changes the whole frame without paint work or tiny-block codec artefacts.
            asm.Literal(2,0x05000000);asm.Emit(0xe1c230b0);asm.Branch("loop",0xe);
        }
        else { asm.Literal(4, 240 * 160); asm.Label("paint"); asm.Emit(0xe0c230b2); asm.Emit(0xe2544001); asm.Branch("paint", 1); asm.Branch("loop", 0xe); }
        var code = asm.Finish(); var rom = new byte[4096];
        BinaryPrimitives.WriteUInt32LittleEndian(rom, 0xea00002e); // Header branch to original code at 0xc0.
        Encoding.ASCII.GetBytes("HOME TEST   ").CopyTo(rom, 0xa0); Encoding.ASCII.GetBytes("HTST").CopyTo(rom, 0xac);
        rom[0xb2] = 0x96; var sum = 0x19; for (var i = 0xa0; i <= 0xbc; i++) sum += rom[i]; rom[0xbd] = (byte)-sum;
        code.CopyTo(rom, 0xc0); return rom;
    }
    private sealed class ArmFixture
    {
        private readonly List<uint> _words = [];
        private readonly Dictionary<string, int> _labels = [];
        private readonly List<(int At, string Label, int Condition)> _branches = [];
        private readonly List<(int At, int Register, uint Value)> _literals = [];
        public void Emit(uint value) => _words.Add(value);
        public void Label(string name) => _labels.Add(name, _words.Count);
        public void Branch(string label, int condition) { _branches.Add((_words.Count, label, condition)); Emit(0); }
        public void Literal(int register, uint value) { _literals.Add((_words.Count, register, value)); Emit(0); }
        public void Halfword(uint address, uint value) { Literal(0, address); Literal(1, value); Emit(0xe1c010b0); }
        public void Move(int register, uint value, int condition)
        {
            for (var rotate = 0; rotate < 16; rotate++)
            {
                var immediate = BitOperations.RotateLeft(value, rotate * 2);
                if (immediate <= 255) { Emit(((uint)condition << 28) | 0x03a00000 | ((uint)register << 12) | ((uint)rotate << 8) | immediate); return; }
            }
            throw new InvalidOperationException("Test instruction immediate cannot be encoded.");
        }
        public byte[] Finish()
        {
            foreach (var branch in _branches) _words[branch.At] = ((uint)branch.Condition << 28) | 0x0a000000 | ((uint)(_labels[branch.Label] - branch.At - 2) & 0x00ffffff);
            foreach (var literal in _literals)
            {
                var offset = (_words.Count - literal.At - 2) * 4;
                if (offset is < 0 or > 4095) throw new InvalidOperationException("Test literal pool is out of range.");
                _words[literal.At] = 0xe59f0000 | ((uint)literal.Register << 12) | (uint)offset; _words.Add(literal.Value);
            }
            var result = new byte[_words.Count * 4]; for (var i = 0; i < _words.Count; i++) BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(i * 4), _words[i]); return result;
        }
    }
}
