"""Original ARM11 3DSX diagnostic; no Nintendo ROM, BIOS or system data.

Public 3DS service interfaces initialize dual framebuffers/HID. Only isolated
native checks use the counters and marker; never part of the product catalog.
"""
from pathlib import Path
import struct
import sys

BASE=0x00100000
class Arm:
    def __init__(self):self.words,self.labels,self.branches,self.literals=[],{},[],[]
    def emit(self,value):self.words.append(value)
    def load(self,reg,value):self.literals.append((len(self.words),reg,value));self.emit(0)
    def label(self,name):self.labels[name]=len(self.words)
    def branch(self,name,cond=14):self.branches.append((len(self.words),name,cond));self.emit(0)
    def svc(self,value):self.emit(0xef000000|value)
    def command(self,handle,values,send=True):
        for index,value in enumerate(values):self.load(0,value);self.emit(0xe58c0000|index*4)
        if send:self.emit(0xe1a00000|handle);self.svc(0x32)
    def finish(self):
        for at,name,cond in self.branches:self.words[at]=cond<<28|0x0a000000|((self.labels[name]-at-2)&0xffffff)
        for at,reg,value in self.literals:
            offset=(len(self.words)-at-2)*4
            if not 0<=offset<4096:raise RuntimeError('Original ARM literal range')
            self.words[at]=0xe59f0000|reg<<12|offset;self.words.append(value)
        return struct.pack('<'+'I'*len(self.words),*self.words)

asm=Arm();asm.emit(0xee1dcf70);asm.emit(0xe28cc080) # TLS command buffer
asm.load(1,BASE+0x900);asm.svc(0x2d);asm.emit(0xe1a08001) # srv handle
asm.command(8,[0x10002,0x20,0])
def service(name,reg):
    data=name.encode().ljust(8,b'\0');a,b=struct.unpack('<II',data)
    asm.command(8,[0x50100,a,b,len(name),0]);asm.emit(0xe59c000c|(reg<<12))
service('gsp::Gpu',9);service('hid:USER',7)
asm.command(9,[0x160042,0,0,0xffff8001])
asm.load(1,0);asm.svc(0x17);asm.emit(0xe1a06001) # one-shot event
asm.command(9,[0x130042,1,0,0],False);asm.emit(0xe58c600c);asm.emit(0xe1a00009);asm.svc(0x32)
asm.command(7,[0xa0000]);asm.emit(0xe59c000c);asm.load(1,0x10000000);asm.load(2,1);asm.load(3,0x10000000);asm.svc(0x1f)
asm.load(0,0x10003);asm.load(1,0);asm.load(2,0);asm.load(3,0x100000);asm.load(4,3);asm.svc(1);asm.emit(0xe1a0a001) # linear RGB565 buffers
asm.command(9,[0xb0040,0])
asm.load(11,BASE+0x1020);asm.load(5,0);asm.load(6,0)
asm.label('frame');asm.load(0,16666666);asm.load(1,0);asm.svc(0x0a)
asm.emit(0xe2855001)
asm.load(0,0x10000000);asm.emit(0xe5901010);asm.emit(0xe1a01201);asm.emit(0xe0800001);asm.emit(0xe5901028) # current HID pad sample
asm.emit(0xe3110001);asm.emit(0x12866001)
asm.emit(0xe58b5000);asm.emit(0xe58b6004)
asm.load(0,0x10000000);asm.emit(0xe59010b8);asm.emit(0xe1a01181);asm.emit(0xe0800001);asm.emit(0xe59020cc) # touchscreen valid flag
asm.emit(0xe58b2008)
asm.load(3,0x07e0);asm.load(4,0xf800);asm.emit(0xe3110001);asm.emit(0x11a03004)
asm.emit(0xe1a0000a);asm.load(1,400*240)
asm.label('top');asm.emit(0xe0c030b2);asm.emit(0xe2511001);asm.branch('top',1)
asm.load(0,0x40000);asm.emit(0xe08a0000);asm.load(1,320*240);asm.load(3,0x001f)
asm.label('bottom');asm.emit(0xe0c030b2);asm.emit(0xe2511001);asm.branch('bottom',1)
for screen in (0,1):
    asm.command(9,[0x50200,screen,0,0,0,480,2,0,0],False)
    if screen:asm.load(0,0x40000);asm.emit(0xe08a0000)
    else:asm.emit(0xe1a0000a)
    asm.emit(0xe58c000c);asm.emit(0xe58c0010);asm.emit(0xe1a00009);asm.svc(0x32)
asm.branch('frame')
code=bytearray(0x2000);payload=asm.finish()
if len(payload)>0x900:raise RuntimeError('Original code overlaps service name')
code[:len(payload)]=payload;code[0x900:0x905]=b'srv:\0'
marker=b'HOME-ORIGINAL-3DS-RAM-PROBE-V1!\0';code[0x1000:0x1020]=marker.ljust(32,b'\0')
header=struct.pack('<IHHIIIIII',0x58534433,32,8,0,0,len(code),0,0,0)
# Three segments, no relocations: code linked to the 3DSX default code base.
Path(sys.argv[1]).write_bytes(header+bytes(24)+code)
