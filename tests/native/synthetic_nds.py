"""Original DS ARM9/ARM7 diagnostic. No Nintendo logo/BIOS/commercial game.

Dual-screen colour output, original PCM tone and button/stylus RAM counters.
Only isolated native qualification uses this file.
"""
import math
from pathlib import Path
import struct
import sys

class Arm:
    def __init__(self): self.words, self.labels, self.branches, self.literals = [], {}, [], []
    def emit(self, word): self.words.append(word)
    def label(self, name): self.labels[name] = len(self.words)
    def load(self, reg, value): self.literals.append((len(self.words), reg, value)); self.emit(0)
    def branch(self, name, condition=14): self.branches.append((len(self.words), name, condition)); self.emit(0)
    def word(self, address, value): self.load(0,address); self.load(1,value); self.emit(0xe5801000)
    def half(self, address, value): self.load(0,address); self.load(1,value); self.emit(0xe1c010b0)
    def byte(self, address, value): self.load(0,address); self.load(1,value); self.emit(0xe5c01000)
    def finish(self):
        for at,name,cond in self.branches: self.words[at] = cond << 28 | 0x0a000000 | ((self.labels[name]-at-2)&0xffffff)
        for at,reg,value in self.literals:
            distance=(len(self.words)-at-2)*4
            if not 0 <= distance <4096: raise RuntimeError('Original fixture literal span')
            self.words[at]=0xe59f0000 | reg<<12 | distance; self.words.append(value)
        return struct.pack('<'+'I'*len(self.words),*self.words)

arm9=Arm()
arm9.half(0x04000304,0x820f)
arm9.byte(0x04000240,0x80)
arm9.byte(0x04000242,0x84)
arm9.word(0x04000000,0x00020000)
arm9.word(0x04001000,0x00010405)
arm9.half(0x0400100c,0x4080)
arm9.half(0x04001020,256);arm9.half(0x04001026,256)
arm9.emit(0xe3a05000);arm9.emit(0xe3a06000);arm9.emit(0xe3a07000)
arm9.label('frame');arm9.load(0,0x04000006)
arm9.label('leaveblank');arm9.emit(0xe1d010b0);arm9.emit(0xe35100c0);arm9.branch('leaveblank',10)
arm9.label('enterblank');arm9.emit(0xe1d010b0);arm9.emit(0xe35100c0);arm9.branch('enterblank',11)
arm9.load(0,0x04000130);arm9.emit(0xe1d010b0);arm9.emit(0xe3110001)
arm9.emit(0xe2855001);arm9.emit(0x02866001)
arm9.load(3,0x83e0);arm9.load(4,0x801f);arm9.emit(0x01a03004)
arm9.load(0,0x04000136);arm9.emit(0xe1d010b0);arm9.emit(0xe3110040);arm9.emit(0x02877001)
arm9.load(0,0x02000820);arm9.emit(0xe5805000);arm9.emit(0xe5806004);arm9.emit(0xe5807008)
arm9.load(2,0x06800000);arm9.load(4,256*192)
arm9.label('painttop');arm9.emit(0xe0c230b2);arm9.emit(0xe2544001);arm9.branch('painttop',1)
arm9.load(2,0x06200000);arm9.load(3,0xfc00);arm9.load(4,256*192)
arm9.label('paintbottom');arm9.emit(0xe0c230b2);arm9.emit(0xe2544001);arm9.branch('paintbottom',1);arm9.branch('frame')
arm7=Arm();arm7.half(0x04000500,0x807f)
arm7.word(0x04000404,0x02000c00);arm7.half(0x04000408,0xfe00)
arm7.half(0x0400040a,0);arm7.word(0x0400040c,256)
arm7.word(0x04000400,0xa840007f);arm7.label('idle');arm7.branch('idle')
rom=bytearray(0x4000);rom[:12]=b'HOME TEST DS';rom[12:16]=b'HTDS'
struct.pack_into('<IIII',rom,0x20,0x200,0x02000000,0x02000000,0x1000)
struct.pack_into('<IIII',rom,0x30,0x2000,0x03800000,0x03800000,0x1000)
struct.pack_into('<II',rom,0x80,len(rom),0x200)
code9,code7=arm9.finish(),arm7.finish();rom[0x200:0x200+len(code9)]=code9;rom[0x2000:0x2000+len(code7)]=code7
marker=b'HOME-ORIGINAL-NDS-RAM-PROBE-V1!\0'
if len(marker)>32:raise RuntimeError('Original marker width')
rom[0xa00:0xa20]=marker.ljust(32,b'\0')
for index in range(512):struct.pack_into('<h',rom,0xe00+index*2,round(8000*math.sin(index*2*math.pi/64)))
def crc16(data):
    value=0xffff
    for byte in data:
        value^=byte
        for _ in range(8):value=(value>>1)^ (0xa001 if value&1 else 0)
    return value
struct.pack_into('<H',rom,0x15c,crc16(rom[0xc0:0x15c]));struct.pack_into('<H',rom,0x15e,crc16(rom[:0x15e]))
Path(sys.argv[1]).write_bytes(rom)
