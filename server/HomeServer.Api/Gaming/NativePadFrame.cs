using HomeServer.Core;

namespace HomeServer.Gaming;
public sealed record NativePointer(short X=0,short Y=0,bool Pressed=false,byte MouseButtons=0,int MotionX=0,int MotionY=0,int Wheel=0,bool Offscreen=false);
public readonly record struct NativePadFrame(ushort Buttons,short LeftX=0,short LeftY=0,short RightX=0,short RightY=0,short PointerX=0,short PointerY=0,bool Pressed=false,string Keyboard="",string Pressure="",byte MouseButtons=0,int MotionX=0,int MotionY=0,int Wheel=0,bool Offscreen=false)
{
    public static NativePadFrame From(ushort buttons,short[]? axes,NativePointer? pointer=null,ushort[]? keys=null,short[]? pressure=null)
    {
        if(axes is not null&&axes.Length!=4)throw new DomainException("Oyun analog girdisi geçersiz.");
        if(keys is not null&&(keys.Length>64||keys.Any(key=>key>=323)))throw new DomainException("Oyun klavye girdisi geçersiz.");
        if(pressure is not null&&(pressure.Length!=16||pressure.Any(value=>value<0)))throw new DomainException("Oyun analog tuş girdisi geçersiz.");
        if(pointer is not null&&(pointer.MouseButtons>7||pointer.MotionX is <-1000000000 or >1000000000||pointer.MotionY is <-1000000000 or >1000000000||pointer.Wheel is <-1000000000 or >1000000000))throw new DomainException("Oyun fare girdisi geçersiz.");
        var result=axes is null?new NativePadFrame(buttons):new(buttons,axes[0],axes[1],axes[2],axes[3]);
        return result with{PointerX=pointer?.X??0,PointerY=pointer?.Y??0,Pressed=pointer?.Pressed??false,Keyboard=keys is null?"":string.Join(',',keys.Distinct().Order()),Pressure=pressure is null||pressure.All(value=>value==0)?"":string.Join(',',pressure),MouseButtons=pointer?.MouseButtons??0,MotionX=pointer?.MotionX??0,MotionY=pointer?.MotionY??0,Wheel=pointer?.Wheel??0,Offscreen=pointer?.Offscreen??false};
    }
    public bool SameActions(NativePadFrame other)=>Buttons==other.Buttons&&Pressed==other.Pressed&&MouseButtons==other.MouseButtons&&(Keyboard??"")==(other.Keyboard??"");
    public bool SameState(NativePadFrame other)=>(this with{Keyboard=Keyboard??"",Pressure=Pressure??""})==(other with{Keyboard=other.Keyboard??"",Pressure=other.Pressure??""});
}
