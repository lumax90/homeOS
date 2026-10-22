using HomeServer.Core;

namespace HomeServer.Gaming;

/// <summary>Bounded input edges; a short press/release cannot vanish between native relay ticks.</summary>
public sealed class NativeInputBuffer
{
    private readonly List<NativePadFrame> _edges=new();
    public NativePadFrame DesiredFrame{get;private set;}
    public NativePadFrame NextDesired=>_edges.Count>0?_edges[0]:DesiredFrame;
    public ushort Desired=>DesiredFrame.Buttons;
    public void Stage(ushort buttons,ushort allowed=RetroPadProtocol.GbaButtons)
        =>StageFrame(new(buttons),allowed);
    public void StageFrame(NativePadFrame frame,ushort allowed=RetroPadProtocol.GbaButtons)
    {
        RetroPadProtocol.Validate(frame.Buttons,allowed);
        if(frame.SameState(DesiredFrame))return;
        if(_edges.Count>0&&_edges[^1].SameActions(frame)){DesiredFrame=frame;_edges[^1]=frame;return;}
        if (_edges.Count >= 32) { Clear(); throw new DomainException("Oyun girdisi kuyruğu doldu; tuşları bırakıp tekrar dene.", 429); }
        DesiredFrame=frame;_edges.Add(frame);
    }
    public ushort Next(bool neutral)
        =>NextFrame(neutral).Buttons;
    public NativePadFrame NextFrame(bool neutral)
    {
        if(neutral){Clear();return default;}
        if(_edges.Count==0)return DesiredFrame;var frame=_edges[0];_edges.RemoveAt(0);return frame;
    }
    public void Clear(){_edges.Clear();DesiredFrame=default;}
}
