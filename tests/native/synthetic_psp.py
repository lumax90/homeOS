"""Original isolated PSP diagnostic ELF, not a commercial ROM or internet fixture.

Uses public PSP HLE display/controller calls, a changing framebuffer and RAM
counters so software video, held buttons/analog and state restoration can be
observed against the actual PPSSPP core. Never appears in the product library.
"""
import struct
import math
from pathlib import Path
import sys

BASE = 0x08804000
words, labels, patches = [], {}, []

def emit(value): words.append(value & 0xffffffff)
def load(reg, value):
    emit(0x3c000000 | reg << 16 | (value >> 16 & 0xffff))
    emit(0x34000000 | reg << 21 | reg << 16 | (value & 0xffff))
def imm(op, target, source, value): emit(op << 26 | source << 21 | target << 16 | (value & 0xffff))
def jump(label, link=False):
    patches.append((len(words), label, 'jump'))
    emit(0x0c000000 if link else 0x08000000); emit(0)
def branch(op, a, b, label):
    patches.append((len(words), label, 'branch'))
    emit(op << 26 | a << 21 | b << 16); emit(0)
def label(name): labels[name] = BASE + len(words) * 4
def call(address): emit(0x0c000000 | (address >> 2 & 0x03ffffff)); emit(0)

# Imports and RAM are fixed within this original ELF's one load segment.
DISPLAY, CTRL, AUDIO = BASE + 0x1900, BASE + 0x1920, BASE + 0x1940
COUNTERS, PAD = BASE + 0x3020, BASE + 0x3100
load(4, 0xffffffff); load(5, 512); load(6, 0); call(AUDIO)
emit(0x0040b021)  # addu s6, v0, zero: retained channel id
load(4, 0); load(5, 480); load(6, 272); call(DISPLAY)
load(4, 0x44000000); load(5, 512); load(6, 3); load(7, 1); call(DISPLAY + 8)
load(4, 1); call(CTRL)  # sceCtrlSetSamplingMode(analog)
label('frame')
emit(0x02c02021); load(5, 0x8000); load(6, BASE + 0x3200); call(AUDIO + 8)
load(4, PAD); load(5, 1); call(CTRL + 8)
load(16, COUNTERS); load(17, PAD)
imm(35, 8, 16, 0); imm(9, 8, 8, 1); imm(43, 8, 16, 0)
imm(35, 9, 17, 4); imm(12, 9, 9, 0x4000)
load(18, 0x000000ff)
branch(4, 9, 0, 'notheld')
imm(35, 8, 16, 4); imm(9, 8, 8, 1); imm(43, 8, 16, 4)
load(18, 0x0000ff00)
label('notheld')
imm(36, 8, 17, 8); imm(43, 8, 16, 8)
load(19, 0x44000000); load(20, 512 * 272)
label('fill')
imm(43, 18, 19, 0); imm(9, 19, 19, 4); imm(9, 20, 20, -1)
branch(5, 20, 0, 'fill')
call(DISPLAY + 16); jump('frame')

for at, target, kind in patches:
    address = labels[target]
    if kind == 'jump': words[at] |= address >> 2 & 0x03ffffff
    else: words[at] |= ((address - (BASE + at * 4 + 4)) // 4) & 0xffff

segment = bytearray(0x4000)
segment[0x3000:0x3020] = b'HOME-ORIGINAL-PSP-RAM-PROBE-V1!\0'
struct.pack_into('<' + 'I' * len(words), segment, 0, *words)
# SceModuleInfo: user module, no exports, three 20-byte import tables.
struct.pack_into('<HH28sIIIII', segment, 0x1000, 0, 0x0100, b'HOME-ISOLATED-PSP', 0, 0, 0, BASE + 0x1800, BASE + 0x183c)
segment[0x1840:0x184b] = b'sceDisplay\0'
segment[0x1860:0x1868] = b'sceCtrl\0'
segment[0x1870:0x1879] = b'sceAudio\0'
struct.pack_into('<IHHBBHII', segment, 0x1800, BASE + 0x1840, 0, 0x4001, 5, 0, 3, BASE + 0x1880, DISPLAY)
struct.pack_into('<IHHBBHII', segment, 0x1814, BASE + 0x1860, 0, 0x4001, 5, 0, 2, BASE + 0x18a0, CTRL)
struct.pack_into('<IHHBBHII', segment, 0x1828, BASE + 0x1870, 0, 0x4001, 5, 0, 2, BASE + 0x18b0, AUDIO)
struct.pack_into('<III', segment, 0x1880, 0x0e20f177, 0x289d82fe, 0x984c27e7)
struct.pack_into('<II', segment, 0x18a0, 0x1f4011e6, 0x1f803938)
struct.pack_into('<II', segment, 0x18b0, 0x5ec81c55, 0x136caf51)
for offset in (0x1900, 0x1908, 0x1910, 0x1920, 0x1928, 0x1940, 0x1948): struct.pack_into('<II', segment, offset, 0x03e00008, 0)
for index in range(512):
    sample = round(8000 * math.sin(index * 2 * math.pi / 64))
    struct.pack_into('<hh', segment, 0x3200 + index * 4, sample, sample)
names = b'\0.text\0.rodata.sceModuleInfo\0.data\0.shstrtab\0'
file_offset, section_offset = 0x100, 0x4200
header = bytearray(52)
header[:16] = b'\x7fELF\x01\x01\x01' + bytes(9)
struct.pack_into('<HHIIIIIHHHHHH', header, 16, 2, 8, 1, BASE, 52, section_offset, 0x20924001, 52, 32, 1, 40, 5, 4)
program = struct.pack('<IIIIIIII', 1, file_offset, BASE, BASE, len(segment), len(segment), 7, 0x100)
result = bytearray(section_offset + 5 * 40)
result[:52], result[52:84] = header, program
result[file_offset:file_offset + len(segment)] = segment
result[0x4100:0x4100 + len(names)] = names
sections = [(0,0,0,0,0,0,0,0,0,0),
    (1,1,6,BASE,file_offset,0x1000,0,0,4,0),
    (7,1,2,BASE+0x1000,file_offset+0x1000,52,0,0,4,0),
    (names.index(b'.data'),1,3,BASE+0x1800,file_offset+0x1800,0x2800,0,0,4,0),
    (names.index(b'.shstrtab'),3,0,0,0x4100,len(names),0,0,1,0)]
for index, section in enumerate(sections): struct.pack_into('<IIIIIIIIII', result, section_offset + index * 40, *section)
Path(sys.argv[1]).write_bytes(result)
